#!/usr/bin/python3 -I
"""End-to-end test of OpenParallel for Linux, run inside a Linux machine (Ali's Parallels VM).

Installs the build with its own install.sh, sets up stand-in apps (shell wrappers around one
fixture program, named and laid out like the real apps), then drives opctl and the menu
entries it writes, and checks every result against what the stand-ins recorded. Real
Chromium / Chrome / Firefox are exercised too when the machine has them.

    python3 linux_test.py <folder with OpenParallel/ and fixture/>   (scripts/linux-test.sh does this)
"""
import glob
import json
import os
import shutil
import signal
import subprocess
import sys
import time

BUILD = os.path.abspath(sys.argv[1])
HOME = os.path.expanduser("~")
DATA = os.environ.get("XDG_DATA_HOME") or os.path.join(HOME, ".local", "share")
APPS_DIR = os.path.join(DATA, "applications")
INSTALL = os.path.join(HOME, ".local", "opt", "OpenParallel")
OPCTL = os.path.join(INSTALL, "opctl")
INSTANCES = os.path.join(DATA, "OpenParallel", "Instances")
TRASH = os.path.join(DATA, "Trash")
FIXTURES = os.path.join(HOME, "op-fixtures")
LOGS = os.path.join(FIXTURES, "_logs")

results = []


def check(name, condition, detail=""):
    results.append((name, bool(condition)))
    print(("PASS " if condition else "FAIL ") + name + ("" if condition else f"  [{detail}]"), flush=True)


def run(*args, ok=True, env=None, timeout=60):
    r = subprocess.run(list(args), capture_output=True, text=True, timeout=timeout, env=env, stdin=subprocess.DEVNULL)
    if ok and r.returncode != 0:
        raise AssertionError(f"{' '.join(args)} failed ({r.returncode}): {r.stderr.strip()[-400:]}")
    return r


def opctl(*args, ok=True):
    r = run(OPCTL, *args, ok=ok)
    out = r.stdout.strip()
    try:
        return (json.loads(out) if out[:1] in "[{" else None), r
    except json.JSONDecodeError:
        return None, r


def records():
    out = []
    for f in glob.glob(os.path.join(LOGS, "*.json")):
        with open(f) as fh:
            out.extend(json.loads(line) for line in fh if line.strip())
    return out


def wait_for(predicate, seconds=20):
    end = time.time() + seconds
    while time.time() < end:
        value = predicate()
        if value:
            return value
        time.sleep(0.3)
    return predicate()


def started_with(fragment):
    return [r for r in records() if r["event"] == "start" and fragment in (r.get("profile") or "")]


def closed(pid):
    return any(r["event"] == "closed" and r["pid"] == pid for r in records())


def launch_entry(path):
    """Starts a menu entry the way the desktop does (gtk-launch / gio), else runs its Exec."""
    desktop_id = os.path.basename(path)
    env = dict(os.environ, OP_FIXTURE_LOG=LOGS)
    # The launched app inherits the launcher's output, so pipes would never close: no capture.
    quiet = dict(stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, env=env, timeout=60)
    if shutil.which("gtk-launch") and path.startswith(APPS_DIR):
        return subprocess.run(["gtk-launch", desktop_id], **quiet).returncode == 0
    if shutil.which("gio"):
        return subprocess.run(["gio", "launch", path], **quiet).returncode == 0
    return False


# MARK: setup

FIXTURE_APPS = {
    # entry name: (wrapper path relative to FIXTURES, marker files beside it)
    "Fake Chromium": ("chromium/chromium", []),
    "Notes": ("notes/notes", ["resources/app.asar"]),
    "Fake Firefox": ("firefox/firefox", ["application.ini", "omni.ja"]),
    "Telegram": ("telegram/telegram-desktop", []),
    "Fake Code": ("code/code", ["resources/app/product.json"]),
    "Fake PyCharm": ("pycharm/bin/pycharm.sh", ["idea.properties"]),
    "Tool": ("tool/tool", []),
    "Zoom": ("zoom/zoom", []),
}


def setup():
    print("== installing the build and stand-in apps", flush=True)
    for pid_file in glob.glob(os.path.join(LOGS, "*.json")):
        os.remove(pid_file)
    run("sh", os.path.join(BUILD, "OpenParallel", "install.sh"))
    shutil.rmtree(FIXTURES, ignore_errors=True)
    runtime = os.path.join(FIXTURES, "_runtime")
    shutil.copytree(os.path.join(BUILD, "fixture"), runtime)
    os.chmod(os.path.join(runtime, "fakelinuxapp"), 0o755)
    os.makedirs(LOGS)
    os.makedirs(APPS_DIR, exist_ok=True)
    for name, (relative, markers) in FIXTURE_APPS.items():
        wrapper = os.path.join(FIXTURES, relative)
        os.makedirs(os.path.dirname(wrapper), exist_ok=True)
        with open(wrapper, "w") as f:
            f.write(f'#!/bin/sh\nOP_FIXTURE_LOG="{LOGS}" exec "{runtime}/fakelinuxapp" "$@"\n')
        os.chmod(wrapper, 0o755)
        for marker in markers:
            path = os.path.join(os.path.dirname(wrapper), marker)
            os.makedirs(os.path.dirname(path), exist_ok=True)
            open(path, "w").close()
        entry = os.path.join(APPS_DIR, f"op-fixture-{relative.split('/')[0]}.desktop")
        with open(entry, "w") as f:
            f.write(f"[Desktop Entry]\nType=Application\nName={name}\nExec={wrapper} %U\nIcon=openparallel\nTerminal=false\n")
    # A clean slate: no instances from earlier runs.
    for m in (opctl("list", ok=False)[0] or []):
        opctl("quit", m["instanceSlug"], "--force", ok=False)
        opctl("trash", m["instanceSlug"], ok=False)


def entry(fixture):
    return os.path.join(APPS_DIR, f"op-fixture-{fixture}.desktop")


# MARK: scenarios

def main():
    setup()
    check("install.sh put OpenParallel in ~/.local/opt", os.access(os.path.join(INSTALL, "OpenParallel"), os.X_OK))
    check("install.sh added the menu entry", os.path.isfile(os.path.join(APPS_DIR, "openparallel.desktop")))
    check("install.sh added ~/.local/bin/openparallel", os.path.islink(os.path.join(HOME, ".local", "bin", "openparallel")))

    print("== detection", flush=True)
    expectations = {
        "chromium": ("Chromium", "Supported"), "notes": ("Electron", "Supported"), "firefox": ("Firefox", "Supported"),
        "telegram": ("Telegram", "Supported"), "code": ("VSCode", "Supported"), "pycharm": ("JetBrains", "Experimental"),
        "tool": ("Generic", "Experimental"), "zoom": ("Unsupported", "NotSupported"),
    }
    for fixture, (family, status) in expectations.items():
        info, _ = opctl("inspect", entry(fixture))
        check(f"inspect {fixture} -> {family}/{status}", info and info["family"] == family and info["status"] == status, info)
    apps, _ = opctl("apps")
    names = {a["name"] for a in apps or []}
    check("apps lists the menu entries", {"Notes", "Tool", "Fake Chromium"} <= names, sorted(names)[:15])
    check("apps leaves out OpenParallel itself", "OpenParallel" not in names)

    print("== two Electron instances", flush=True)
    a, _ = opctl("create", "--app", entry("notes"), "--name", "Notes Parall")
    b, _ = opctl("create", "--app", entry("notes"), "--name", "Notes Parall")
    check("second instance gets the next free name", b["instanceName"] == "Notes Parall 2", b["instanceName"])
    with open(a["shortcutPath"]) as f:
        text = f.read()
    check("menu entry is openparallel-<slug>.desktop", a["shortcutPath"].endswith("openparallel-notes-parall.desktop"))
    check("menu entry starts the app with its own --user-data-dir", "--user-data-dir=" in text and "notes-parall/electron" in text, text)
    check("menu entry carries StartupWMClass", "StartupWMClass=OpenParallel.notes-parall" in text, text)
    launched = launch_entry(a["shortcutPath"])
    rec = wait_for(lambda: started_with("/Instances/notes-parall/electron"))
    check("opening the menu entry starts Notes with its own profile", rec, f"launched={launched}")
    check("and with --class for the dock", rec and "--class=OpenParallel.notes-parall" in rec[0]["args"], rec)
    opctl("launch", "notes-parall-2")
    rec2 = wait_for(lambda: started_with("/Instances/notes-parall-2/electron"))
    check("the second instance gets a different profile", rec2)
    original = subprocess.Popen([os.path.join(FIXTURES, "notes", "notes")], env=dict(os.environ, OP_FIXTURE_LOG=LOGS))
    time.sleep(1.5)
    running, _ = opctl("running")
    check("both instances are recognised as running", running and "notes-parall" in running and "notes-parall-2" in running, running)
    check("the original app is not mistaken for an instance",
          running and original.pid not in sum(running.values(), []), running)

    print("== quit and Trash", flush=True)
    opctl("quit", "notes-parall")
    check("Quit closes the instance (SIGTERM)", wait_for(lambda: rec and closed(rec[0]["pid"]), 10))
    _, r = opctl("trash", "notes-parall-2", ok=False)
    check("Trash is refused while the instance runs", r.returncode != 0 and "Quit" in (r.stderr + r.stdout), r.stderr)
    opctl("quit", "notes-parall-2")
    wait_for(lambda: rec2 and closed(rec2[0]["pid"]), 10)
    opctl("trash", "notes-parall-2")
    check("its data folder went to the Trash", os.path.isdir(os.path.join(TRASH, "files", "notes-parall-2")))
    info_file = os.path.join(TRASH, "info", "notes-parall-2.trashinfo")
    check("with a .trashinfo saying where it came from",
          os.path.isfile(info_file) and "Path=" + INSTANCES.replace(" ", "%20") in open(info_file).read())
    check("its menu entry went too", not os.path.exists(b["shortcutPath"]))

    print("== JetBrains (environment in the menu entry)", flush=True)
    jb, _ = opctl("create", "--app", entry("pycharm"), "--name", "PyCharm Work")
    with open(jb["shortcutPath"]) as f:
        text = f.read()
    check("menu entry sets PYCHARM_PROPERTIES with env", "Exec=env PYCHARM_PROPERTIES=" in text, text)
    launch_entry(jb["shortcutPath"])
    rec = wait_for(lambda: [r for r in records() if r["event"] == "start" and "pycharm-work" in json.dumps(r.get("environment"))])
    check("PyCharm starts with its own properties file", rec and rec[0]["environment"].get("PYCHARM_PROPERTIES", "").endswith("pycharm-work/jetbrains/idea.properties"), rec)
    props = os.path.join(INSTANCES, "pycharm-work", "jetbrains", "idea.properties")
    check("the properties file points into the instance", os.path.isfile(props) and "pycharm-work/jetbrains/config" in open(props).read())
    running, _ = opctl("running")
    check("recognised as running through its environment", running and "pycharm-work" in running, running)

    print("== any other app (XDG folders)", flush=True)
    tool, _ = opctl("create", "--app", entry("tool"), "--name", "Tool Two")
    opctl("launch", "tool-two")
    rec = wait_for(lambda: [r for r in records() if r["event"] == "start" and "tool-two" in json.dumps(r.get("environment"))])
    env = rec[0]["environment"] if rec else {}
    check("generic app gets its own XDG_CONFIG_HOME/DATA/CACHE/STATE",
          all(env.get(k, "").endswith(f"tool-two/xdg/{v}") for k, v in
              [("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data"), ("XDG_CACHE_HOME", "cache"), ("XDG_STATE_HOME", "state")]), env)
    running, _ = opctl("running")
    check("recognised as running", running and "tool-two" in running, running)

    print("== Firefox, Telegram, VS Code", flush=True)
    for fixture, name, expect in [
        ("firefox", "Firefox Two", ["-profile", "-no-remote", "--class=OpenParallel.firefox-two"]),
        ("telegram", "Telegram Two", ["-many", "-workdir"]),
        ("code", "Code Two", ["--user-data-dir", "--extensions-dir"]),
    ]:
        m, _ = opctl("create", "--app", entry(fixture), "--name", name)
        opctl("launch", m["instanceSlug"])
        rec = wait_for(lambda: started_with(f"/Instances/{m['instanceSlug']}/"))
        check(f"{fixture}: starts with {' '.join(expect)}", rec and all(e in rec[0]["args"] for e in expect), rec)

    print("== refused and websites", flush=True)
    _, r = opctl("create", "--app", entry("zoom"), "--name", "Zoom Two", ok=False)
    check("Zoom is refused with the reason", r.returncode != 0 and "only one" in (r.stderr + r.stdout), r.stderr)
    web, _ = opctl("create", "--web", "https://web.whatsapp.com", "--name", "WhatsApp Web", "--browser", entry("chromium"))
    opctl("launch", web["instanceSlug"])
    rec = wait_for(lambda: started_with("/Instances/whatsapp-web/web"))
    check("website opens as an app window with its own profile",
          rec and "--app=https://web.whatsapp.com" in rec[0]["args"] and "--class=OpenParallel.whatsapp-web" in rec[0]["args"], rec)

    print("== real browsers on this machine", flush=True)
    real_browsers()

    print("== clean up", flush=True)
    original.terminate()
    listed, _ = opctl("list")
    for m in listed or []:
        opctl("quit", m["instanceSlug"], ok=False)
    time.sleep(2)
    for m in listed or []:
        opctl("quit", m["instanceSlug"], "--force", ok=False)
        opctl("trash", m["instanceSlug"], ok=False)
    remaining, _ = opctl("list")
    check("no instances left after Move to Trash", remaining == [], remaining)
    check("no OpenParallel menu entries left", not glob.glob(os.path.join(APPS_DIR, "openparallel-*.desktop")))
    for f in glob.glob(os.path.join(APPS_DIR, "op-fixture-*.desktop")):
        os.remove(f)

    passed = sum(1 for _, ok in results if ok)
    print(f"\n{passed}/{len(results)} checks passed", flush=True)
    return 0 if passed == len(results) else 1


def real_browsers():
    """Chromium, Chrome or Firefox from the distribution, a Snap or a Flatpak, if present."""
    _, r = opctl("apps")
    apps = json.loads(r.stdout) if r.stdout.strip().startswith("[") else []
    picks = [a for a in apps if a["family"] in ("Chromium", "Firefox") and not os.path.basename(a["exePath"]).startswith("op-fixture")]
    if not picks:
        print("SKIP no real Chromium or Firefox on this machine", flush=True)
        return
    if not os.environ.get("DISPLAY") and not os.environ.get("WAYLAND_DISPLAY"):
        print("SKIP real browsers need a desktop session (DISPLAY)", flush=True)
        return
    for app in picks[:2]:
        m, _ = opctl("create", "--app", app["exePath"], "--name", app["name"] + " Test")
        opctl("launch", m["instanceSlug"])
        running = wait_for(lambda: (opctl("running")[0] or {}).get(m["instanceSlug"]), 40)
        check(f"real {app['name']}: the instance runs and is recognised", running, m["launchArguments"])
        # The app must really use its folder: a Snap Firefox once ran but showed "profile missing".
        args = m["launchArguments"]
        profile = (args[args.index("-profile") + 1] if "-profile" in args
                   else next((a.split("=", 1)[1] for a in args if a.startswith("--user-data-dir=")), None))
        filled = profile and wait_for(lambda: os.path.isdir(profile) and len(os.listdir(profile)) > 3, 40)
        check(f"real {app['name']}: it writes its own profile ({profile})", filled, os.listdir(profile) if profile and os.path.isdir(profile) else "missing")
        opctl("quit", m["instanceSlug"])
        gone = wait_for(lambda: not (opctl("running")[0] or {}).get(m["instanceSlug"]), 30)
        check(f"real {app['name']}: Quit closes it", gone)


if __name__ == "__main__":
    sys.exit(main())
