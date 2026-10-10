using System;
using System.Linq;
using System.Windows.Forms;

namespace ChargeGuard
{
    /*
     * SteamVR Charge Guard entry point.
     *
     *   (no arguments)        install / uninstall window
     *   --run                 background watcher (what the sign-in entry starts)
     *   --run --dry-run       watcher that only logs what it would do
     *   --uninstall           uninstall with a confirmation prompt (Apps & features)
     *   --install --quiet     install without any window; exit code 0 = ok
     *   --uninstall --quiet   uninstall without any window
     *   --lang ko|ja|en       force the UI language
     */
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            int lang = Array.IndexOf(args, "--lang");
            if (lang >= 0 && lang + 1 < args.Length && Strings.Has(args[lang + 1]))
                Strings.Lang = args[lang + 1];

            if (args.Contains("--run"))
                return Watcher.RunMain(args.Contains("--dry-run"));

            if (args.Contains("--quiet"))
            {
                try
                {
                    if (args.Contains("--install")) Installer.Install();
                    else if (args.Contains("--uninstall")) Installer.Uninstall();
                }
                catch (InstallError e)
                {
                    return e.Key == "err_close_steamvr" ? 3 : e.Key == "err_no_steamvr" ? 4 : 2;
                }
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Contains("--uninstall"))
                MainForm.UninstallFromSettings();
            else
                Application.Run(new MainForm());
            return 0;
        }
    }
}
