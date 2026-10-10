using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ChargeGuard
{
    /// <summary>Small install / uninstall window.</summary>
    sealed class MainForm : Form
    {
        static readonly Color Good = Color.FromArgb(0x1a, 0x7f, 0x37);
        static readonly Color Bad = Color.FromArgb(0xb4, 0x23, 0x18);
        static readonly Color Muted = Color.FromArgb(0x57, 0x60, 0x6a);

        readonly Label programValue, steamValue, vrValue;
        readonly Button installButton, uninstallButton;
        readonly Timer timer = new Timer { Interval = 2000 };

        public MainForm()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = $"{App.Name} {App.Version}";
            Font = new Font(Strings.Font, 10F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(20, 16, 20, 16);
            try { Icon = Icon.ExtractAssociatedIcon(App.ThisExe); } catch (ArgumentException) { }

            var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
            Controls.Add(layout);

            layout.Controls.Add(new Label
            {
                Text = App.Name,
                Font = new Font(Strings.Font, 15F, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8),
            });
            layout.Controls.Add(new Label
            {
                Text = Strings.T("desc"),
                AutoSize = true,
                MaximumSize = new Size(470, 0),
                Margin = new Padding(0, 0, 0, 14),
            });

            var status = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(10, 8, 10, 8),
                Margin = new Padding(0),
            };
            programValue = AddStatusRow(status, "program");
            steamValue = AddStatusRow(status, "steam_autolaunch");
            vrValue = AddStatusRow(status, "steamvr");
            layout.Controls.Add(status);

            var buttons = new TableLayoutPanel { ColumnCount = 4, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0) };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            installButton = MakeButton("install", OnInstall);
            uninstallButton = MakeButton("uninstall", OnUninstall);
            buttons.Controls.Add(installButton, 0, 0);
            buttons.Controls.Add(uninstallButton, 1, 0);
            buttons.Controls.Add(MakeButton("open_log", OnOpenLog), 3, 0);
            layout.Controls.Add(buttons);

            timer.Tick += (s, e) => RefreshStatus();
            Load += (s, e) => { RefreshStatus(); timer.Start(); };
            FormClosed += (s, e) => timer.Dispose();
        }

        static Label AddStatusRow(TableLayoutPanel table, string key)
        {
            table.Controls.Add(new Label { Text = Strings.T(key), ForeColor = Muted, AutoSize = true, Margin = new Padding(0, 3, 24, 3) });
            var value = new Label { AutoSize = true, Font = new Font(Strings.Font, 10F, FontStyle.Bold), Margin = new Padding(0, 3, 0, 3) };
            table.Controls.Add(value);
            return value;
        }

        static Button MakeButton(string key, EventHandler onClick)
        {
            var button = new Button
            {
                Text = Strings.T(key),
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 2, 8, 2),
                Margin = new Padding(0, 0, 8, 0),
                UseVisualStyleBackColor = true,
            };
            button.Click += onClick;
            return button;
        }

        static void Show(Label label, string key, Color color)
        {
            label.Text = Strings.T(key);
            label.ForeColor = color;
        }

        void RefreshStatus()
        {
            bool installed = Installer.IsInstalled();
            if (installed)
            {
                bool running = Installer.WatcherRunning();
                Show(programValue, running ? "installed_running" : "installed_stopped", running ? Good : Bad);
            }
            else
            {
                Show(programValue, "not_installed", Muted);
            }
            bool steamOn = SteamVR.ReadAutoLaunch() != false;
            Show(steamValue, steamOn ? "on" : "off", installed ? (steamOn ? Bad : Good) : Muted);
            Show(vrValue, SteamVR.IsRunning() ? "running" : "not_running", Muted);
            installButton.Text = Strings.T(installed ? "reinstall" : "install");
            uninstallButton.Enabled = installed;
        }

        bool RunAction(Action action, string doneKey)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                action();
            }
            catch (InstallError e)
            {
                MessageBox.Show(this, Strings.T(e.Key), Strings.T("error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            catch (Exception e)
            {
                MessageBox.Show(this, $"{e.GetType().Name}: {e.Message}", Strings.T("error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                Cursor = Cursors.Default;
                RefreshStatus();
            }
            MessageBox.Show(this, Strings.T(doneKey), App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        void OnInstall(object sender, EventArgs e) => RunAction(Installer.Install, "install_done");

        void OnUninstall(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, Strings.T("uninstall_confirm"), App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            if (RunAction(Installer.Uninstall, "uninstall_done")) Close();
        }

        void OnOpenLog(object sender, EventArgs e)
        {
            string target = File.Exists(App.LogFile) ? App.LogFile
                : Directory.Exists(App.InstallDir) ? App.InstallDir
                : Path.GetDirectoryName(App.InstallDir);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }

        /// <summary>Entry for Windows "Apps &amp; features" -> Uninstall.</summary>
        public static void UninstallFromSettings()
        {
            if (MessageBox.Show(Strings.T("uninstall_confirm"), App.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            try
            {
                Installer.Uninstall();
            }
            catch (InstallError e)
            {
                MessageBox.Show(Strings.T(e.Key), Strings.T("error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            MessageBox.Show(Strings.T("uninstall_done"), App.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
