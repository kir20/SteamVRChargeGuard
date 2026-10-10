using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace ChargeGuard
{
    static class App
    {
        public const string Name = "SteamVR Charge Guard";
        public const string Id = "SteamVRChargeGuard";
        public const string Version = "1.1.0";
        public const string ExeName = Id + ".exe";
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Id;
        public const string WatcherMutex = @"Local\" + Id;
        public const string StopEvent = @"Local\" + Id + ".Stop";

        public static readonly string InstallDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Id);
        public static readonly string InstalledExe = Path.Combine(InstallDir, ExeName);
        public static readonly string StateFile = Path.Combine(InstallDir, "install.json");
        public static readonly string LogFile = Path.Combine(InstallDir, "launcher.log");

        public static string ThisExe => Process.GetCurrentProcess().MainModule.FileName;

        public static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'),
                          StringComparison.OrdinalIgnoreCase);
    }

    static class Log
    {
        const long MaxBytes = 1_000_000;
        static readonly object Gate = new object();
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static void Info(string message)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(App.InstallDir);
                    var info = new FileInfo(App.LogFile);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        File.Delete(App.LogFile + ".2");
                        if (File.Exists(App.LogFile + ".1")) File.Move(App.LogFile + ".1", App.LogFile + ".2");
                        File.Move(App.LogFile, App.LogFile + ".1");
                    }
                    File.AppendAllText(App.LogFile,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss,fff") + " " + message + "\r\n", Utf8);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <summary>Where SteamVR lives and its "auto-launch when a device turns on" setting.</summary>
    static class SteamVR
    {
        const string Section = "power";
        const string AutoLaunchKey = "autoLaunchSteamVROnButtonPress";
        const string DefaultSteamDir = @"C:\Program Files (x86)\Steam";
        static readonly string[] ProcessNames = { "vrserver", "vrmonitor", "vrstartup" };
        static readonly Encoding Utf8 = new UTF8Encoding(false);

        static IEnumerable<string> OpenVrPaths(string name)
        {
            JsonObject paths = null;
            try
            {
                string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                           "openvr", "openvrpaths.vrpath");
                paths = Json.Parse(File.ReadAllText(file)) as JsonObject;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException) { }
            if (paths?.Get(name) is List<object> list)
                return list.OfType<string>();
            return Enumerable.Empty<string>();
        }

        static string SteamDir()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (key?.GetValue("SteamPath") is string path && path.Length > 0)
                    return Path.GetFullPath(path);
            }
            return DefaultSteamDir;
        }

        /// <summary>SteamVR install folder, or null if SteamVR is not installed.</summary>
        public static string Runtime()
        {
            var candidates = OpenVrPaths("runtime").ToList();
            candidates.Add(Path.Combine(SteamDir(), "steamapps", "common", "SteamVR"));
            return candidates.FirstOrDefault(r => File.Exists(StartupExe(r)));
        }

        public static string StartupExe(string runtime) => Path.Combine(runtime, "bin", "win64", "vrstartup.exe");

        public static string SettingsPath()
        {
            foreach (string config in OpenVrPaths("config"))
            {
                string path = Path.Combine(config, "steamvr.vrsettings");
                if (File.Exists(path)) return path;
            }
            return Path.Combine(SteamDir(), "config", "steamvr.vrsettings");
        }

        public static bool IsRunning()
        {
            foreach (string name in ProcessNames)
            {
                Process[] found = Process.GetProcessesByName(name);
                foreach (var p in found) p.Dispose();
                if (found.Length > 0) return true;
            }
            return false;
        }

        /// <summary>Steam's own auto-launch: true, false, or null when unset (SteamVR's default is on).</summary>
        public static bool? ReadAutoLaunch()
        {
            string path = SettingsPath();
            if (!File.Exists(path)) return null;
            try
            {
                var settings = Json.Parse(File.ReadAllText(path)) as JsonObject;
                return (settings?.Get(Section) as JsonObject)?.Get(AutoLaunchKey) as bool?;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException)
            {
                return null;
            }
        }

        /// <summary>Sets Steam's own auto-launch (null removes the key). SteamVR must not be running.</summary>
        public static void WriteAutoLaunch(bool? value)
        {
            string path = SettingsPath();
            JsonObject settings = File.Exists(path) ? Json.Parse(File.ReadAllText(path)) as JsonObject : null;
            if (settings == null) settings = new JsonObject();

            string backup = path + ".chargeguard-backup";
            if (File.Exists(path) && !File.Exists(backup)) File.Copy(path, backup);

            var section = settings.Get(Section) as JsonObject;
            if (value.HasValue)
            {
                if (section == null) settings.Set(Section, section = new JsonObject());
                section.Set(AutoLaunchKey, value.Value);
            }
            else if (section != null)
            {
                section.Remove(AutoLaunchKey);
                if (section.Items.Count == 0) settings.Remove(Section);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Json.Write(settings), Utf8);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
