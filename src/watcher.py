"""Background watcher: launches SteamVR when a lighthouse device powers on, but not when it powers on because it was plugged in to charge.

Steam's own watchdog starts SteamVR whenever any device connects to a dongle,
including a controller or tracker that switched itself on on the charger. The
installer turns that off (steamvr.vrsettings "power" ->
"autoLaunchSteamVROnButtonPress": false) and this watcher takes over.

How charging is detected: after powering on (and while SteamVR is not running)
a Watchman device sends a v1 status event (E1 <battery>) every 5 s, the first
one ~4 s after power-on. Bit 0x80 of the battery byte is set while it is on a
charging cable (GripVR: 0xE4 charging, 0x64 on battery).

- Power-on while not charging -> launch SteamVR.
- Power-on while charging -> do nothing.
- A device that powered on from the charger and is then unplugged -> launch SteamVR.
- A connection only counts as a power-on after the dongle saw a turn-off report
  (0x26) or 60 s of silence, so a weak radio link that drops and reconnects is ignored.
"""
import ctypes
import logging
import logging.handlers
import os
import subprocess
import threading
import time

import hid

import common

VALVE_VID = 0x28DE
DONGLE_PIDS = (0x2101, 0x2102)  # Watchman Dongle, Valve VR Radio

SILENCE_GAP = 3.0        # seconds without reports = link down
REBOOT_GAP = 60.0        # silence this long (or a turn-off report) before a connection counts as a power-on
DECIDE_TIMEOUT = 12.0    # no battery event this long after power-on -> treat as normal power-on
LAUNCH_COOLDOWN = 30.0   # give a launch this long to show up before allowing another
STARTUP_REPORTS_LOGGED = 6

REPORT_RF_WATCHMAN = 0x23
REPORT_RF_WATCHMAN_X2 = 0x24
REPORT_RF_TURN_OFF = 0x26

log = logging.getLogger('watcher')


def setup_logging():
    os.makedirs(common.INSTALL_DIR, exist_ok=True)
    handler = logging.handlers.RotatingFileHandler(
        common.LOG_FILE, maxBytes=1_000_000, backupCount=2, encoding='utf-8')
    handler.setFormatter(logging.Formatter('%(asctime)s %(message)s'))
    log.addHandler(handler)
    log.setLevel(logging.INFO)


def battery_bytes(report):
    """Yield battery status bytes from a Watchman v1 RF report."""
    rid = report[0]
    if rid == REPORT_RF_WATCHMAN:
        packets = [report[1:30]]
    elif rid == REPORT_RF_WATCHMAN_X2:
        packets = [report[1:30], report[30:59]]
    else:
        return
    for pkt in packets:
        if len(pkt) < 5:
            continue
        size = pkt[1]
        payload = pkt[3:3 + size - 1]
        # Status event: 111 0 I 0 0 b (b = battery byte follows, I = IMU data after it)
        if len(payload) >= 2 and (payload[0] & 0xF7) == 0xE1:
            yield payload[1]


class Dongle:
    def __init__(self, serial, app):
        self.serial, self.app = serial, app
        self.label = 'Dongle ' + serial
        self.last_report = 0.0
        self.turned_off = True     # nothing seen yet counts as off
        self.link_up = False
        self.connected_at = 0.0
        self.decided = True
        # Battery bytes are only trusted while the device is still in the v1
        # protocol it boots into; SteamVR switches it to another protocol.
        self.trusted = False
        self.battery = None
        self.startup_logged = 0

    def forget_battery(self):
        self.trusted, self.battery = False, None

    def on_report(self, report, now):
        if report[0] == REPORT_RF_TURN_OFF:
            if self.link_up:
                log.info('%s: turned off', self.label)
            self.turned_off, self.link_up = True, False
            self.forget_battery()
            return
        if not self.link_up or now - self.last_report > SILENCE_GAP:
            power_on = self.turned_off or now - self.last_report >= REBOOT_GAP
            self.link_up, self.turned_off = True, False
            self.connected_at = now
            self.startup_logged = 0
            if not power_on:
                self.decided = True
                log.info('%s: link came back after %.0fs (radio dropout, ignored)',
                         self.label, now - self.last_report)
            else:
                # Power-ons that happen while SteamVR runs are SteamVR's business.
                self.decided = self.app.steamvr_running
                self.trusted, self.battery = not self.app.steamvr_running, None
                log.info('%s: powered on%s', self.label, ' (SteamVR running)' if self.decided else '')
        self.last_report = now
        if not self.trusted:
            return
        if not self.decided and self.startup_logged < STARTUP_REPORTS_LOGGED:
            self.startup_logged += 1
            log.info('%s:   report %s', self.label, bytes(report).hex())
        for status in battery_bytes(report):
            self.on_battery(status)
            return

    def on_battery(self, status):
        charging = bool(status & 0x80)
        percent = status & 0x7F
        prev, self.battery = self.battery, status
        if not self.decided:
            self.decided = True
            log.info('%s: battery %d%% (0x%02X), %s', self.label, percent, status,
                     'charging -> not launching SteamVR' if charging else 'not charging -> normal power-on')
            if not charging:
                self.app.launch(self.label)
        elif prev is not None and prev & 0x80 and not charging and abs((prev & 0x7F) - percent) <= 3:
            log.info('%s: unplugged from charger (battery %d%%) -> launching SteamVR', self.label, percent)
            self.app.launch(self.label + ' unplugged')
        elif prev is not None and not prev & 0x80 and charging:
            log.info('%s: plugged into charger (battery %d%%)', self.label, percent)

    def tick(self, now):
        if not self.link_up:
            return
        if now - self.last_report > SILENCE_GAP:
            self.link_up = False
            log.info('%s: link down', self.label)
        elif not self.decided and now - self.connected_at > DECIDE_TIMEOUT:
            self.decided = True
            log.info('%s: no battery status within %.0fs -> treating as normal power-on', self.label, DECIDE_TIMEOUT)
            self.app.launch(self.label)


class Watcher:
    def __init__(self, dry_run=False):
        self.dry_run = dry_run
        self.dongles = {}
        self.readers = {}
        self.lock = threading.Lock()
        self.steamvr_running = False
        self.last_launch = 0.0
        self.launch_pending = False

    def launch(self, why):
        now = time.time()
        if self.steamvr_running or now - self.last_launch < LAUNCH_COOLDOWN:
            return
        self.last_launch = now
        if self.dry_run:
            log.info('[dry-run] would launch SteamVR (%s)', why)
            return
        runtime = common.steamvr_runtime()
        if runtime is None:
            log.info('WARNING: SteamVR is not installed, cannot launch (%s)', why)
            return
        # Start vrstartup.exe directly, like Steam's watchdog does. Going through
        # steam://rungameid/250820 silently does nothing while Steam still counts
        # SteamVR as running (e.g. an overlay app left over from the last session).
        exe = os.path.join(runtime, 'bin', 'win64', 'vrstartup.exe')
        log.info('launching SteamVR (%s): %s', why, exe)
        self.launch_pending = True
        subprocess.Popen([exe], cwd=os.path.dirname(exe), close_fds=True,
                         creationflags=subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP)

    def reader(self, dongle, path):
        try:
            dev = hid.device()
            dev.open_path(path)
        except OSError as e:
            log.info('%s: open failed: %s', dongle.label, e)
            return
        log.info('%s: listening', dongle.label)
        try:
            while True:
                report = dev.read(64, 1000)
                if report:
                    with self.lock:
                        dongle.on_report(report, time.time())
        except OSError as e:
            log.info('%s: dongle read stopped: %s', dongle.label, e)
        finally:
            dev.close()

    def ensure_readers(self):
        for info in hid.enumerate(VALVE_VID):
            serial = info['serial_number']
            if info['product_id'] not in DONGLE_PIDS or not serial:
                continue
            thread = self.readers.get(serial)
            if thread is None or not thread.is_alive():
                dongle = self.dongles.setdefault(serial, Dongle(serial, self))
                thread = threading.Thread(target=self.reader, args=(dongle, info['path']), daemon=True)
                self.readers[serial] = thread
                thread.start()

    def check_steamvr(self):
        running = common.steamvr_running()
        if running != self.steamvr_running:
            log.info('SteamVR %s', 'started' if running else 'stopped')
            self.steamvr_running = running
            if running:
                self.launch_pending = False
                with self.lock:
                    for d in self.dongles.values():
                        d.decided = True
                        d.forget_battery()
        return running

    def keep_steam_autolaunch_off(self):
        """Re-apply the setting if something turned Steam's auto-launch back on."""
        if self.steamvr_running or common.read_autolaunch() is False:
            return
        try:
            common.write_autolaunch(False)
            log.info("Steam's own auto-launch was on again; turned it off")
        except (OSError, ValueError) as e:
            log.info("WARNING: could not turn off Steam's own auto-launch: %s", e)

    def run(self):
        log.info('%s %s watcher started%s', common.APP_NAME, common.VERSION, ' (dry-run)' if self.dry_run else '')
        last_scan = 0.0
        last_settings_check = 0.0
        while True:
            now = time.time()
            if now - last_scan > 5:
                last_scan = now
                self.ensure_readers()
            self.check_steamvr()
            if now - last_settings_check > 60:
                last_settings_check = now
                self.keep_steam_autolaunch_off()
            if self.launch_pending and now - self.last_launch > LAUNCH_COOLDOWN:
                self.launch_pending = False
                log.info('WARNING: SteamVR did not start within %.0fs of the launch', LAUNCH_COOLDOWN)
            with self.lock:
                for d in list(self.dongles.values()):
                    d.tick(now)
            time.sleep(1)


def run(dry_run=False):
    setup_logging()
    kernel32 = ctypes.windll.kernel32
    mutex = kernel32.CreateMutexW(None, False, 'Local\\' + common.APP_ID)
    if kernel32.GetLastError() == 183:  # ERROR_ALREADY_EXISTS
        log.info('another watcher is already running, exiting')
        return
    try:
        Watcher(dry_run).run()
    except Exception:
        log.exception('watcher crashed')
        raise
    finally:
        kernel32.CloseHandle(mutex)
