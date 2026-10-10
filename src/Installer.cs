using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace ChargeGuard
{
    /// <summary>Thrown with a Strings key describing why install / uninstall could not run.</summary>
    sealed class InstallError : Exception
    {
        public readonly string Key;
        public InstallError(string key) : base(key) { Key = key; }
    }

    /// <summary>Copies the exe, turns off Steam's own auto-launch and registers the watcher at sign-in.</summary>
    static class Installer
    {
        public static bool IsInstalled() => File.Exists(App.InstalledExe) && RunEntry() != null;

        public static bool WatcherRunning()
        {
            if (!Mutex.TryOpenExisting(App.WatcherMutex, out Mutex mutex)) return false;
            mutex.Dispose();
            return true;
        }

        static string RunEntry()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(App.RunKey))
                return key?.GetValue(App.Id) as string;
        }

        static void SetRunEntry(string command)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(App.RunKey))
            {
                if (command == null) key.DeleteValue(App.Id, false);
                else key.SetValue(App.Id, command);
            }
        }

        static void SetUninstallEntry(bool present)
        {
            if (!present)
            {
                Registry.CurrentUser.DeleteSubKeyTree(App.UninstallKey, false);
                return;
            }
            using (var key = Registry.CurrentUser.CreateSubKey(App.UninstallKey))
            {
                key.SetValue("DisplayName", App.Name);
                key.SetValue("DisplayVersion", App.Version);
                key.SetValue("Publisher", App.Name);
                key.SetValue("DisplayIcon", App.InstalledExe);
                key.SetValue("InstallLocation", App.InstallDir);
                key.SetValue("UninstallString", $"\"{App.InstalledExe}\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize", (int)(new FileInfo(App.InstalledExe).Length / 1024), RegistryValueKind.DWord);
            }
        }

        /// <summary>Asks the running watcher to exit; force-stops older builds that don't listen for the event.</summary>
        public static void StopWatchers()
        {
            if (EventWaitHandle.TryOpenExisting(App.StopEvent, out EventWaitHandle stop))
            {
                stop.Set();
                stop.Dispose();
            }
            var waited = Stopwatch.StartNew();
            // 8 s: the watcher first lets its radio wake-up child release the dongles (up to 5 s)
            while (WatcherRunning() && waited.Elapsed.TotalSeconds < 8) Thread.Sleep(200);
            if (!WatcherRunning()) return;

            int me = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcessesByName(App.Id))
            {
                using (p)
                {
                    // Watchers have no window; leave any open install windows alone.
                    if (p.Id == me || p.MainWindowHandle != IntPtr.Zero) continue;
                    try
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                    catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception) { }
                }
            }
        }

        static void StartWatcher()
        {
            Process.Start(new ProcessStartInfo(App.InstalledExe, "--run")
            {
                UseShellExecute = true,
                WorkingDirectory = App.InstallDir,
            })?.Dispose();
        }

        static JsonObject LoadState()
        {
            try
            {
                return Json.Parse(File.ReadAllText(App.StateFile)) as JsonObject ?? new JsonObject();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException)
            {
                return new JsonObject();
            }
        }

        public static void Install()
        {
            if (SteamVR.Runtime() == null) throw new InstallError("err_no_steamvr");
            if (SteamVR.IsRunning()) throw new InstallError("err_close_steamvr");

            StopWatchers();
            Directory.CreateDirectory(App.InstallDir);
            if (!App.SamePath(App.ThisExe, App.InstalledExe))
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        File.Copy(App.ThisExe, App.InstalledExe, true);
                        break;
                    }
                    catch (IOException) when (attempt < 10)
                    {
                        Thread.Sleep(500);   // an old watcher may need a moment to release the file
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                    {
                        throw new InstallError("err_copy");
                    }
                }
            }

            JsonObject state = LoadState();
            if (state.Get("original_autolaunch") == null)
            {
                bool? original = SteamVR.ReadAutoLaunch();
                state.Set("original_autolaunch", original.HasValue ? (object)original.Value : JsonNull.Value);
            }
            state.Set("version", App.Version);
            File.WriteAllText(App.StateFile, Json.Write(state));
            try
            {
                SteamVR.WriteAutoLaunch(false);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException)
            {
                throw new InstallError("err_settings");
            }

            SetRunEntry($"\"{App.InstalledExe}\" --run");
            SetUninstallEntry(true);
            StartWatcher();
        }

        public static void Uninstall()
        {
            if (SteamVR.IsRunning()) throw new InstallError("err_close_steamvr");
            StopWatchers();
            SetRunEntry(null);
            SetUninstallEntry(false);

            bool? original = LoadState().Get("original_autolaunch") as bool?;
            try
            {
                SteamVR.WriteAutoLaunch(original);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException)
            {
                throw new InstallError("err_settings");
            }

            if (App.SamePath(Path.GetDirectoryName(App.ThisExe), App.InstallDir))
            {
                // Can't delete the folder we're running from; let cmd do it after we exit.
                Process.Start(new ProcessStartInfo("cmd.exe",
                    $"/c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"{App.InstallDir}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                })?.Dispose();
            }
            else
            {
                try { Directory.Delete(App.InstallDir, true); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }
    }
}
