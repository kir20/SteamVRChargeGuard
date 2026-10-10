using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace ChargeGuard
{
    /*
     * Keeps the dongle radios awake while SteamVR is closed.
     *
     * After a reboot the Watchman dongles do not let a paired controller or tracker connect until
     * something on the PC starts them up. Normally that is Steam's watchdog, which runs SteamVR's
     * lighthouse driver module "IVRWatchdogProvider" inside Steam. But Steam only runs it while
     * "autoLaunchSteamVROnButtonPress" is on, and turning that off is the whole point of this app.
     * So without this, nothing can connect after a reboot until SteamVR has been started once.
     *
     * The fix runs that same module from the user's own SteamVR install (driver_lighthouse.dll) in
     * a child process ("--radio-host"), and tells it that auto-launch is on. Its wake-up callback is
     * only logged: the watcher still decides from the battery status whether to start SteamVR.
     * The child is stopped whenever SteamVR runs or is being launched, so the two never share the
     * dongles, and it runs in its own process so a crash in the driver cannot take the watcher down.
     */
    static class RadioWake
    {
        const string Prefix = "@@CG ";

        // ───────────────────────── child process (--radio-host) ─────────────────────────

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)] static extern IntPtr GetProcAddress(IntPtr module, string name);

        // OpenVR driver interfaces (openvr_driver.h); on x64 "this" is simply the first argument.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr FactoryFn([MarshalAs(UnmanagedType.LPStr)] string name, out int error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitFn(IntPtr self, IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void CleanupFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetGenericInterfaceFn(IntPtr self, IntPtr name, IntPtr error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate ulong GetDriverHandleFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void WakeUpFn(IntPtr self, int deviceClass);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void LogFn(IntPtr self, IntPtr message);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ErrorNameFn(IntPtr self, int error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetBoolFn(IntPtr self, IntPtr section, IntPtr key, byte value, IntPtr error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetInt32Fn(IntPtr self, IntPtr section, IntPtr key, int value, IntPtr error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetFloatFn(IntPtr self, IntPtr section, IntPtr key, float value, IntPtr error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void SetStringFn(IntPtr self, IntPtr section, IntPtr key, IntPtr value, IntPtr error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate byte GetBoolFn(IntPtr self, IntPtr section, IntPtr key, IntPtr error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetInt32Fn(IntPtr self, IntPtr section, IntPtr key, IntPtr error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate float GetFloatFn(IntPtr self, IntPtr section, IntPtr key, IntPtr error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void GetStringFn(IntPtr self, IntPtr section, IntPtr key, IntPtr buffer, uint length, IntPtr error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void RemoveSectionFn(IntPtr self, IntPtr section, IntPtr error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void RemoveKeyFn(IntPtr self, IntPtr section, IntPtr key, IntPtr error);

        const int InitErrorInterfaceNotFound = 105;   // VRInitError_Init_InterfaceNotFound
        const int SettingsErrorUnset = 5;             // VRSettingsError_UnsetSettingHasNoDefault

        // The native side holds pointers to these delegates for as long as the process lives.
        static readonly List<Delegate> Pinned = new List<Delegate>();

        static IntPtr NativeObject(params Delegate[] methods)
        {
            IntPtr vtable = Marshal.AllocHGlobal(IntPtr.Size * methods.Length);
            for (int i = 0; i < methods.Length; i++)
            {
                Pinned.Add(methods[i]);
                Marshal.WriteIntPtr(vtable, i * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(methods[i]));
            }
            IntPtr obj = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(obj, vtable);
            return obj;
        }

        static string Str(IntPtr p) => p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p);

        static void SetError(IntPtr error, int value)
        {
            if (error != IntPtr.Zero) Marshal.WriteInt32(error, value);
        }

        static void Say(string line)
        {
            Console.Out.WriteLine(Prefix + line);
            Console.Out.Flush();
        }

        public static string DriverPath(string runtime) =>
            Path.Combine(runtime, "drivers", "lighthouse", "bin", "win64", "driver_lighthouse.dll");

        /// <summary>Entry for --radio-host: runs the lighthouse watchdog until stdin closes.</summary>
        public static int ChildMain()
        {
            try
            {
                if (!Environment.Is64BitProcess) { Say("FAILED needs a 64-bit Windows"); return 2; }
                string runtime = SteamVR.Runtime();
                string dll = runtime == null ? null : DriverPath(runtime);
                if (dll == null || !File.Exists(dll)) { Say("FAILED SteamVR lighthouse driver not found"); return 2; }

                SetDllDirectory(Path.GetDirectoryName(dll));
                IntPtr module = LoadLibrary(dll);
                if (module == IntPtr.Zero) { Say($"FAILED could not load {dll} ({Marshal.GetLastWin32Error()})"); return 3; }
                IntPtr factoryPtr = GetProcAddress(module, "HmdDriverFactory");
                if (factoryPtr == IntPtr.Zero) { Say("FAILED HmdDriverFactory not found"); return 3; }
                var factory = (FactoryFn)Marshal.GetDelegateForFunctionPointer(factoryPtr, typeof(FactoryFn));
                IntPtr provider = factory("IVRWatchdogProvider_001", out int factoryError);
                if (provider == IntPtr.Zero) { Say($"FAILED no IVRWatchdogProvider_001 ({factoryError})"); return 3; }

                IntPtr errorName = Marshal.StringToHGlobalAnsi("VRSettingsError");
                var interfaces = new Dictionary<string, IntPtr>
                {
                    ["IVRWatchdogHost_002"] = NativeObject(new WakeUpFn((self, deviceClass) => Say("WAKE " + deviceClass))),
                    ["IVRDriverLog_001"] = NativeObject(new LogFn((self, message) => Say("LOG " + Str(message).TrimEnd()))),
                    // Settings: report Steam's auto-launch as on so the module does its work; nothing else is set.
                    ["IVRSettings_003"] = NativeObject(
                        new ErrorNameFn((self, error) => errorName),
                        new SetBoolFn((self, section, key, value, error) => SetError(error, 0)),
                        new SetInt32Fn((self, section, key, value, error) => SetError(error, 0)),
                        new SetFloatFn((self, section, key, value, error) => SetError(error, 0)),
                        new SetStringFn((self, section, key, value, error) => SetError(error, 0)),
                        new GetBoolFn((self, section, key, error) =>
                        {
                            bool autoLaunch = Str(section) == "power" && Str(key) == "autoLaunchSteamVROnButtonPress";
                            SetError(error, autoLaunch ? 0 : SettingsErrorUnset);
                            return (byte)(autoLaunch ? 1 : 0);
                        }),
                        new GetInt32Fn((self, section, key, error) => { SetError(error, SettingsErrorUnset); return 0; }),
                        new GetFloatFn((self, section, key, error) => { SetError(error, SettingsErrorUnset); return 0f; }),
                        new GetStringFn((self, section, key, buffer, length, error) =>
                        {
                            if (buffer != IntPtr.Zero && length > 0) Marshal.WriteByte(buffer, 0);
                            SetError(error, SettingsErrorUnset);
                        }),
                        new RemoveSectionFn((self, section, error) => SetError(error, 0)),
                        new RemoveKeyFn((self, section, key, error) => SetError(error, 0))),
                };
                IntPtr context = NativeObject(
                    new GetGenericInterfaceFn((self, name, error) =>
                    {
                        bool found = interfaces.TryGetValue(Str(name), out IntPtr obj);
                        SetError(error, found ? 0 : InitErrorInterfaceNotFound);
                        return found ? obj : IntPtr.Zero;
                    }),
                    new GetDriverHandleFn(self => 1));

                IntPtr vtable = Marshal.ReadIntPtr(provider);
                var init = (InitFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtable, 0), typeof(InitFn));
                var cleanup = (CleanupFn)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vtable, IntPtr.Size), typeof(CleanupFn));
                int result = init(provider, context);
                if (result != 0) { Say("FAILED watchdog init error " + result); return 3; }
                Say("READY");

                // Run until the watcher closes our stdin (or dies, which closes it too).
                while (Console.In.ReadLine() != null) { }
                cleanup(provider);
                Say("STOPPED");
                return 0;
            }
            catch (Exception e)
            {
                Say("FAILED " + e.GetType().Name + ": " + e.Message);
                return 1;
            }
        }

        // ───────────────────────── watcher side ─────────────────────────

        /// <summary>Starts, stops and restarts the --radio-host child for the watcher.</summary>
        public sealed class Supervisor
        {
            const double RestartAfterSteamVR = 8;    // let vrserver let go of the dongles first
            const double StopTimeout = 5;

            readonly object gate = new object();
            Process child;
            double stopRequestedAt = -1;
            double lastSteamVR = -1e9;
            double nextStart = -1e9;
            int failures;

            static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

            /// <summary>Called once a second. busy = SteamVR is running or being launched.</summary>
            public void Update(bool busy)
            {
                double now = Now();
                lock (gate)
                {
                    if (busy)
                    {
                        lastSteamVR = now;
                        RequestStop("SteamVR is running");
                    }
                    if (child != null)
                    {
                        if (child.HasExited)
                        {
                            Reap(now);
                        }
                        else if (stopRequestedAt >= 0 && now - stopRequestedAt > StopTimeout)
                        {
                            try { child.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                            Log.Info("radio wake-up: did not stop in time, killed it");
                            Reap(now);
                        }
                        return;
                    }
                    if (busy || now - lastSteamVR < RestartAfterSteamVR || now < nextStart) return;
                    Start(now);
                }
            }

            /// <summary>Asks the child to let go of the dongles (does not wait).</summary>
            public void RequestStop(string why)
            {
                lock (gate)
                {
                    if (child == null || stopRequestedAt >= 0) return;
                    stopRequestedAt = Now();
                    try { child.StandardInput.Close(); }
                    catch (IOException) { }
                    catch (InvalidOperationException) { }
                }
            }

            /// <summary>Stops the child and waits for it (watcher shutdown).</summary>
            public void StopAndWait()
            {
                Process p;
                lock (gate)
                {
                    RequestStop("watcher stopping");
                    p = child;
                }
                if (p == null) return;
                if (!p.WaitForExit((int)(StopTimeout * 1000)))
                {
                    try { p.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
                }
                lock (gate) Reap(Now());
            }

            void Start(double now)
            {
                var info = new ProcessStartInfo(App.ThisExe, "--radio-host")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                };
                try
                {
                    var p = new Process { StartInfo = info, EnableRaisingEvents = true };
                    p.OutputDataReceived += (sender, e) => OnLine(e.Data);
                    p.Start();
                    p.BeginOutputReadLine();
                    child = p;
                    stopRequestedAt = -1;
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is IOException || e is InvalidOperationException)
                {
                    Log.Info("radio wake-up: could not start: " + e.Message);
                    ScheduleRetry(now);
                }
            }

            void Reap(double now)
            {
                if (child == null) return;
                bool asked = stopRequestedAt >= 0;
                int code = -1;
                try { code = child.ExitCode; } catch (InvalidOperationException) { }
                child.Dispose();
                child = null;
                stopRequestedAt = -1;
                if (asked && code == 0)
                {
                    Log.Info("radio wake-up stopped");
                }
                else
                {
                    Log.Info($"radio wake-up exited unexpectedly (code {code})");
                    ScheduleRetry(now);
                }
            }

            void ScheduleRetry(double now)
            {
                failures++;
                double wait = Math.Min(600, 30 * Math.Pow(2, Math.Min(failures - 1, 5)));
                nextStart = now + wait;
                Log.Info($"radio wake-up: retrying in {wait:0}s");
            }

            /// <summary>
            /// The driver's own output: keep what shows the radios being woken or something failing.
            /// "UserData Error ... out of range" comes from headsets without user data and is harmless.
            /// </summary>
            static bool Interesting(string line)
            {
                string lower = line.ToLowerInvariant();
                if (lower.Contains("userdata")) return false;
                return lower.Contains("keepalive") || lower.Contains("fail") || lower.Contains("error") || lower.Contains("watchdog");
            }

            void OnLine(string line)
            {
                if (line == null) return;
                if (!line.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    if (Interesting(line)) Log.Info("radio wake-up: driver: " + line.Trim());
                    return;
                }
                string message = line.Substring(Prefix.Length);
                if (message == "READY")
                {
                    lock (gate) failures = 0;
                    Log.Info("radio wake-up on (SteamVR's lighthouse watchdog keeps the dongles listening)");
                }
                else if (message.StartsWith("WAKE ", StringComparison.Ordinal))
                {
                    Log.Info($"radio wake-up: a device woke the watchdog (device class {message.Substring(5)})");
                }
                else if (message.StartsWith("LOG ", StringComparison.Ordinal))
                {
                    string text = message.Substring(4);
                    if (Interesting(text)) Log.Info("radio wake-up: driver: " + text);
                }
                else if (message != "STOPPED")
                {
                    Log.Info("radio wake-up: " + message);
                }
            }
        }
    }
}
