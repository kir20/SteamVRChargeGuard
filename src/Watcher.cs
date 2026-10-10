using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ChargeGuard
{
    /*
     * Background watcher: launches SteamVR when a lighthouse device powers on, but not when it
     * powers on because it was plugged in to charge.
     *
     * Steam's own watchdog starts SteamVR whenever any device connects to a dongle, including a
     * controller or tracker that switched itself on on the charger. The installer turns that off
     * (steamvr.vrsettings "power" -> "autoLaunchSteamVROnButtonPress": false) and this takes over.
     *
     * How charging is detected: after powering on (and while SteamVR is not running) a Watchman
     * device sends a v1 status event (E1 <battery>) every 5 s, the first ~4 s after power-on.
     * Bit 0x80 of the battery byte is set while it is on a charging cable (GripVR: 0xE4 charging,
     * 0x64 on battery).
     *
     * - Power-on while not charging -> launch SteamVR.
     * - Power-on while charging -> do nothing.
     * - A device that powered on from the charger and is then unplugged -> launch SteamVR.
     * - A connection only counts as a power-on after the dongle saw a turn-off report (0x26) or
     *   60 s of silence, so a weak radio link that drops and reconnects is ignored.
     */
    sealed class Dongle
    {
        const double SilenceGap = 3;        // seconds without reports = link down
        const double RebootGap = 60;        // silence this long (or a turn-off report) before a connection counts as a power-on
        const double DecideTimeout = 12;    // no battery event this long after power-on -> treat as normal power-on
        const int StartupReportsLogged = 6;

        const byte ReportRfWatchman = 0x23, ReportRfWatchmanX2 = 0x24, ReportRfTurnOff = 0x26;

        readonly Watcher watcher;
        public readonly string Label;

        double lastReport;
        bool turnedOff = true;   // nothing seen yet counts as off
        bool linkUp;
        double connectedAt;
        bool decided = true;
        // Battery bytes are only trusted while the device is still in the v1 protocol it boots
        // into; SteamVR switches it to another protocol.
        bool trusted;
        int? battery;
        int startupLogged;

        public Dongle(string serial, Watcher watcher)
        {
            Label = "Dongle " + serial;
            this.watcher = watcher;
        }

        void ForgetBattery()
        {
            trusted = false;
            battery = null;
        }

        public void OnSteamVRStarted()
        {
            decided = true;
            ForgetBattery();
        }

        public void OnReport(byte[] report, int length, double now)
        {
            if (length < 1) return;
            if (report[0] == ReportRfTurnOff)
            {
                if (linkUp) Log.Info($"{Label}: turned off");
                turnedOff = true;
                linkUp = false;
                ForgetBattery();
                return;
            }
            if (!linkUp || now - lastReport > SilenceGap)
            {
                bool powerOn = turnedOff || now - lastReport >= RebootGap;
                linkUp = true;
                turnedOff = false;
                connectedAt = now;
                startupLogged = 0;
                if (!powerOn)
                {
                    decided = true;
                    Log.Info($"{Label}: link came back after {now - lastReport:0}s (radio dropout, ignored)");
                }
                else
                {
                    // Power-ons that happen while SteamVR runs are SteamVR's business.
                    decided = watcher.SteamVRRunning;
                    trusted = !watcher.SteamVRRunning;
                    battery = null;
                    Log.Info($"{Label}: powered on" + (decided ? " (SteamVR running)" : ""));
                }
            }
            lastReport = now;
            if (!trusted) return;
            if (!decided && startupLogged < StartupReportsLogged)
            {
                startupLogged++;
                Log.Info($"{Label}:   report {BitConverter.ToString(report, 0, length).Replace("-", "").ToLowerInvariant()}");
            }
            foreach (int status in BatteryBytes(report, length))
            {
                OnBattery(status);
                return;
            }
        }

        void OnBattery(int status)
        {
            bool charging = (status & 0x80) != 0;
            int percent = status & 0x7F;
            int? previous = battery;
            battery = status;
            if (!decided)
            {
                decided = true;
                Log.Info($"{Label}: battery {percent}% (0x{status:X2}), " +
                         (charging ? "charging -> not launching SteamVR" : "not charging -> normal power-on"));
                if (!charging) watcher.Launch(Label);
            }
            else if (previous.HasValue && (previous.Value & 0x80) != 0 && !charging &&
                     Math.Abs((previous.Value & 0x7F) - percent) <= 3)
            {
                Log.Info($"{Label}: unplugged from charger (battery {percent}%) -> launching SteamVR");
                watcher.Launch(Label + " unplugged");
            }
            else if (previous.HasValue && (previous.Value & 0x80) == 0 && charging)
            {
                Log.Info($"{Label}: plugged into charger (battery {percent}%)");
            }
        }

        public void Tick(double now)
        {
            if (!linkUp) return;
            if (now - lastReport > SilenceGap)
            {
                linkUp = false;
                Log.Info($"{Label}: link down");
            }
            else if (!decided && now - connectedAt > DecideTimeout)
            {
                decided = true;
                Log.Info($"{Label}: no battery status within {DecideTimeout:0}s -> treating as normal power-on");
                watcher.Launch(Label);
            }
        }

        /// <summary>Battery status bytes in a Watchman v1 RF report.</summary>
        static IEnumerable<int> BatteryBytes(byte[] report, int length)
        {
            int packets = report[0] == ReportRfWatchman ? 1 : report[0] == ReportRfWatchmanX2 ? 2 : 0;
            for (int p = 0; p < packets; p++)
            {
                int start = 1 + p * 29;              // each packet: time MSB, size, time LSB, payload
                if (start + 4 >= length) yield break;
                int size = report[start + 1];
                int payload = start + 3;
                int payloadEnd = Math.Min(payload + size - 1, Math.Min(start + 29, length));
                // Status event: 111 0 I 0 0 b (b = battery byte follows, I = IMU data after it)
                if (payloadEnd - payload >= 2 && (report[payload] & 0xF7) == 0xE1)
                    yield return report[payload + 1];
            }
        }
    }

    sealed class Watcher
    {
        const ushort ValveVendorId = 0x28DE;
        static readonly ushort[] DonglePids = { 0x2101, 0x2102 };   // Watchman Dongle, Valve VR Radio
        const double LaunchCooldown = 30;   // give a launch this long to show up before allowing another

        readonly bool dryRun;
        readonly object gate = new object();
        readonly Dictionary<string, Dongle> dongles = new Dictionary<string, Dongle>();
        readonly Dictionary<string, Thread> readers = new Dictionary<string, Thread>();
        double lastLaunch = -1e9;
        bool launchPending;

        public bool SteamVRRunning { get; private set; }

        Watcher(bool dryRun) { this.dryRun = dryRun; }

        static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

        public void Launch(string why)
        {
            double now = Now();
            if (SteamVRRunning || now - lastLaunch < LaunchCooldown) return;
            lastLaunch = now;
            if (dryRun)
            {
                Log.Info($"[dry-run] would launch SteamVR ({why})");
                return;
            }
            string runtime = SteamVR.Runtime();
            if (runtime == null)
            {
                Log.Info($"WARNING: SteamVR is not installed, cannot launch ({why})");
                return;
            }
            // Start vrstartup.exe directly, like Steam's watchdog does. Going through
            // steam://rungameid/250820 silently does nothing while Steam still counts SteamVR
            // as running (e.g. an overlay app left over from the last session).
            string exe = SteamVR.StartupExe(runtime);
            Log.Info($"launching SteamVR ({why}): {exe}");
            launchPending = true;
            try
            {
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) })?.Dispose();
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is IOException)
            {
                Log.Info($"WARNING: could not start SteamVR: {e.Message}");
            }
        }

        void Read(Dongle dongle, string path)
        {
            FileStream stream;
            int reportLength;
            try
            {
                stream = Hid.OpenForReading(path, out reportLength);
            }
            catch (IOException e)
            {
                Log.Info($"{dongle.Label}: open failed: {e.Message}");
                return;
            }
            Log.Info($"{dongle.Label}: listening");
            using (stream)
            {
                var buffer = new byte[reportLength];
                try
                {
                    while (true)
                    {
                        int n = stream.Read(buffer, 0, buffer.Length);
                        if (n <= 0) break;
                        lock (gate) dongle.OnReport(buffer, n, Now());
                    }
                }
                catch (Exception e) when (e is IOException || e is OperationCanceledException)
                {
                    Log.Info($"{dongle.Label}: dongle read stopped: {e.Message}");
                }
            }
        }

        void EnsureReaders()
        {
            foreach (Hid.Device device in Hid.Enumerate(ValveVendorId))
            {
                if (!DonglePids.Contains(device.ProductId) || device.Serial.Length == 0) continue;
                if (readers.TryGetValue(device.Serial, out Thread thread) && thread.IsAlive) continue;
                Dongle dongle;
                lock (gate)
                {
                    if (!dongles.TryGetValue(device.Serial, out dongle))
                        dongles[device.Serial] = dongle = new Dongle(device.Serial, this);
                }
                string path = device.Path;
                thread = new Thread(() => Read(dongle, path)) { IsBackground = true, Name = dongle.Label };
                readers[device.Serial] = thread;
                thread.Start();
            }
        }

        void CheckSteamVR()
        {
            bool running = SteamVR.IsRunning();
            if (running == SteamVRRunning) return;
            Log.Info("SteamVR " + (running ? "started" : "stopped"));
            lock (gate)
            {
                SteamVRRunning = running;
                if (!running) return;
                launchPending = false;
                foreach (Dongle d in dongles.Values) d.OnSteamVRStarted();
            }
        }

        /// <summary>Turns Steam's own auto-launch back off if something switched it on again.</summary>
        void KeepSteamAutoLaunchOff()
        {
            if (SteamVRRunning || SteamVR.ReadAutoLaunch() == false) return;
            try
            {
                SteamVR.WriteAutoLaunch(false);
                Log.Info("Steam's own auto-launch was on again; turned it off");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException)
            {
                Log.Info($"WARNING: could not turn off Steam's own auto-launch: {e.Message}");
            }
        }

        void Run(WaitHandle stop)
        {
            Log.Info($"{App.Name} {App.Version} watcher started" + (dryRun ? " (dry-run)" : ""));
            double lastScan = -1e9, lastSettingsCheck = -1e9;
            do
            {
                double now = Now();
                if (now - lastScan > 5)
                {
                    lastScan = now;
                    EnsureReaders();
                }
                CheckSteamVR();
                if (now - lastSettingsCheck > 60)
                {
                    lastSettingsCheck = now;
                    KeepSteamAutoLaunchOff();
                }
                lock (gate)
                {
                    if (launchPending && now - lastLaunch > LaunchCooldown)
                    {
                        launchPending = false;
                        Log.Info($"WARNING: SteamVR did not start within {LaunchCooldown:0}s of the launch");
                    }
                    foreach (Dongle d in dongles.Values) d.Tick(now);
                }
            } while (!stop.WaitOne(1000));
            Log.Info("watcher stopped");
        }

        /// <summary>Entry for --run. Only one watcher runs at a time; the installer stops it through a named event.</summary>
        public static int RunMain(bool dryRun)
        {
            using (var mutex = new Mutex(false, App.WatcherMutex, out bool createdNew))
            {
                if (!createdNew)
                {
                    Log.Info("another watcher is already running, exiting");
                    return 0;
                }
                using (var stop = new EventWaitHandle(false, EventResetMode.ManualReset, App.StopEvent))
                {
                    try
                    {
                        new Watcher(dryRun).Run(stop);
                    }
                    catch (Exception e)
                    {
                        Log.Info("watcher crashed: " + e);
                        return 1;
                    }
                }
            }
            return 0;
        }
    }
}
