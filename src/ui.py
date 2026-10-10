"""Small install / uninstall window."""
import ctypes
import os
import sys
import tkinter as tk
from tkinter import messagebox, ttk

import common
import installer
import strings
from strings import t

FONTS = {'ko': 'Malgun Gothic', 'ja': 'Yu Gothic UI', 'en': 'Segoe UI'}
GOOD, BAD, MUTED = '#1a7f37', '#b42318', '#57606a'


class Window:
    def __init__(self):
        try:
            ctypes.windll.shcore.SetProcessDpiAwareness(1)
        except (AttributeError, OSError):
            pass
        self.root = tk.Tk()
        self.root.title(f'{common.APP_NAME} {common.VERSION}')
        self.root.resizable(False, False)
        icon = os.path.join(getattr(sys, '_MEIPASS', os.path.join(os.path.dirname(__file__), '..', 'assets')), 'icon.ico')
        if os.path.exists(icon):
            self.root.iconbitmap(default=icon)
        font = FONTS.get(strings.LANG, 'Segoe UI')
        style = ttk.Style(self.root)
        style.configure('.', font=(font, 10))
        style.configure('Title.TLabel', font=(font, 15, 'bold'))
        style.configure('Muted.TLabel', foreground=MUTED)
        style.configure('Value.TLabel', font=(font, 10, 'bold'))

        frame = ttk.Frame(self.root, padding=(20, 18, 20, 16))
        frame.grid(sticky='nsew')
        ttk.Label(frame, text=common.APP_NAME, style='Title.TLabel').grid(row=0, column=0, columnspan=2, sticky='w')
        ttk.Label(frame, text=t('desc'), wraplength=460, justify='left').grid(
            row=1, column=0, columnspan=2, sticky='w', pady=(8, 14))

        status = ttk.Frame(frame, padding=(12, 8), relief='groove', borderwidth=2)
        status.grid(row=2, column=0, columnspan=2, sticky='ew')
        status.columnconfigure(1, weight=1)
        self.values = {}
        for row, key in enumerate(('program', 'steam_autolaunch', 'steamvr')):
            ttk.Label(status, text=t(key), style='Muted.TLabel').grid(row=row, column=0, sticky='w', padx=(0, 16), pady=2)
            value = ttk.Label(status, style='Value.TLabel')
            value.grid(row=row, column=1, sticky='w', pady=2)
            self.values[key] = value

        buttons = ttk.Frame(frame)
        buttons.grid(row=3, column=0, columnspan=2, sticky='ew', pady=(16, 0))
        self.install_btn = ttk.Button(buttons, command=self.on_install)
        self.install_btn.pack(side='left')
        self.uninstall_btn = ttk.Button(buttons, text=t('uninstall'), command=self.on_uninstall)
        self.uninstall_btn.pack(side='left', padx=(8, 0))
        ttk.Button(buttons, text=t('open_log'), command=self.on_open_log).pack(side='right')

        self.refresh()

    def _set(self, key, text, color):
        self.values[key].configure(text=text, foreground=color)

    def refresh(self):
        installed = installer.is_installed()
        if installed:
            running = installer.watcher_running()
            self._set('program', t('installed_running' if running else 'installed_stopped'), GOOD if running else BAD)
        else:
            self._set('program', t('not_installed'), MUTED)
        autolaunch = common.read_autolaunch()
        steam_on = autolaunch is not False
        self._set('steam_autolaunch', t('on' if steam_on else 'off'), (BAD if steam_on else GOOD) if installed else MUTED)
        vr = common.steamvr_running()
        self._set('steamvr', t('running' if vr else 'not_running'), MUTED)
        self.install_btn.configure(text=t('reinstall' if installed else 'install'))
        self.uninstall_btn.state(['!disabled'] if installed else ['disabled'])
        self.root.after(2000, self.refresh)

    def _run(self, action, done_key):
        self.root.configure(cursor='watch')
        self.root.update()
        try:
            action()
        except installer.InstallError as e:
            messagebox.showerror(t('error'), t(str(e)), parent=self.root)
            return False
        except Exception as e:  # show anything unexpected instead of dying silently
            messagebox.showerror(t('error'), f'{type(e).__name__}: {e}', parent=self.root)
            return False
        finally:
            self.root.configure(cursor='')
        messagebox.showinfo(common.APP_NAME, t(done_key), parent=self.root)
        return True

    def on_install(self):
        self._run(installer.install, 'install_done')

    def on_uninstall(self):
        if messagebox.askyesno(common.APP_NAME, t('uninstall_confirm'), parent=self.root):
            if self._run(installer.uninstall, 'uninstall_done'):
                self.root.destroy()

    def on_open_log(self):
        folder = common.INSTALL_DIR if os.path.isdir(common.INSTALL_DIR) else os.environ['LOCALAPPDATA']
        os.startfile(common.LOG_FILE if os.path.exists(common.LOG_FILE) else folder)

    def run(self):
        self.root.mainloop()


def uninstall_from_settings():
    """Entry for Windows "Apps & features" -> Uninstall."""
    root = tk.Tk()
    root.withdraw()
    if not messagebox.askyesno(common.APP_NAME, t('uninstall_confirm')):
        return
    try:
        installer.uninstall()
    except installer.InstallError as e:
        messagebox.showerror(t('error'), t(str(e)))
        return
    messagebox.showinfo(common.APP_NAME, t('uninstall_done'))
