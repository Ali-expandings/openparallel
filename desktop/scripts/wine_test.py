#!/usr/bin/python3 -I
"""End-to-end test of OpenParallel for Windows inside the Sikarugir Wine bottle.

Installs the build and stand-in apps into the bottle, then drives opctl and checks every
result against what the stand-ins recorded. Run through scripts/wine-test.sh.
"""
import json
import os
import shutil
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WINE = os.environ["OP_WINE"]
PREFIX = os.environ["WINEPREFIX"]
DRIVE_C = os.path.join(PREFIX, "drive_c")
USER = os.path.join(DRIVE_C, "users", "crossover")
LOCAL = os.path.join(USER, "AppData", "Local")
TEMP = os.path.join(USER, "Temp")  # Wine sets %TEMP% to C:\users\<user>\Temp
FIXTURE_LOGS = os.path.join(TEMP, "op-fixtures")
PROGRAMS = os.path.join(USER, "AppData", "Roaming", "Microsoft", "Windows", "Start Menu", "Programs")
COMMON_PROGRAMS = os.path.join(DRIVE_C, "ProgramData", "Microsoft", "Windows", "Start Menu", "Programs")
OP_DIR = os.path.join(DRIVE_C, "OpenParallel")
OPCTL = r"C:\OpenParallel\opctl.exe"
INSTANCES = os.path.join(LOCAL, "OpenParallel", "Instances")

results = []


def unix(path):
    """C:\\a\\b -> <prefix>/drive_c/a/b"""
    assert path[1:3] == ":\\", path
    return os.path.join(DRIVE_C, *path[3:].split("\\"))


def wine(*args, timeout=90):
    """Runs a Windows program. Output goes to temp files, not pipes: Wine's long-lived helper
    processes inherit the handles, so a pipe would never reach end-of-file."""
    import tempfile
    with tempfile.TemporaryFile("w+") as out, tempfile.TemporaryFile("w+") as err:
        code = subprocess.run([WINE, *args], stdout=out, stderr=err, stdin=subprocess.DEVNULL, timeout=timeout).returncode
        out.seek(0)
        err.seek(0)
        return subprocess.CompletedProcess(args, code, out.read(), err.read())


def opctl(*args, ok=True):
    r = wine(OPCTL, *args)
    if ok and r.returncode != 0:
        raise AssertionError(f"opctl {' '.join(args)} failed: {r.stderr.strip()[-400:]}")
    out = r.stdout.strip()
    start = min([i for i in (out.find("{"), out.find("[")) if i >= 0], default=-1)
    try:
        return json.loads(out[start:]) if start >= 0 else None, r
    except json.JSONDecodeError:
        return None, r


def check(name, condition, detail=""):
    results.append((name, bool(condition), detail))
    print(("PASS " if condition else "FAIL ") + name + ("" if condition else f"  [{detail}]"), flush=True)


def records():
    out = []
    if not os.path.isdir(FIXTURE_LOGS):
        return out
    for f in os.listdir(FIXTURE_LOGS):
        with open(os.path.join(FIXTURE_LOGS, f)) as fh:
            for line in fh:
                if line.strip():
                    out.append(json.loads(line))
    return out


def wait_for(predicate, seconds=20):
    end = time.time() + seconds
    while time.time() < end:
        value = predicate()
        if value:
            return value
        time.sleep(0.5)
    return predicate()


def starts_with_profile(fragment):
    return [r for r in records() if r.get("event") == "start" and fragment.lower() in (r.get("profile") or "").lower()]


# MARK: install

FIXTURES = {
    # windows exe path : marker files relative to the exe folder
    r"C:\Program Files\FakeChromium\Application\chrome.exe": ["131.0.0.0\\chrome_elf.dll"],
    r"C:\Program Files\FakeElectron\FakeElectron.exe": ["resources\\app.asar"],
    r"C:\Program Files\FakeFirefox\firefox.exe": ["application.ini", "omni.ja"],
    r"C:\Program Files\Telegram Desktop\Telegram.exe": [],
    r"C:\Program Files\FakeCode\Code.exe": ["resources\\app\\product.json", "resources\\app\\package.json"],
    r"C:\Program Files\FakeJetBrains\bin\pycharm64.exe": ["idea.properties"],
    r"C:\Program Files\FakePlain\plain.exe": [],
    r"C:\Program Files\Zoom\bin\Zoom.exe": [],
    "SQUIRREL_UPDATE": [],
    "C:\\users\\crossover\\AppData\\Local\\FakeSquirrel\\app-1.0.0\\FakeSquirrel.exe": ["resources\\app.asar"],
    "C:\\users\\crossover\\AppData\\Local\\FakeSquirrel\\app-1.1.0\\FakeSquirrel.exe": ["resources\\app.asar"],
}
SQUIRREL_UPDATE = "C:\\users\\crossover\\AppData\\Local\\FakeSquirrel\\Update.exe"


def install_fixture(win_exe, markers, runtime):
    exe = unix(win_exe)
    folder = os.path.dirname(exe)
    os.makedirs(folder, exist_ok=True)
    for name in os.listdir(runtime):
        if name == "FakeApp.exe":
            continue
        link = os.path.join(folder, name)
        if not os.path.lexists(link):
            os.symlink(os.path.join(runtime, name), link)
    shutil.copy2(os.path.join(runtime, "FakeApp.exe"), exe)
    for marker in markers:
        path = os.path.join(folder, *marker.split("\\"))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        open(path, "w").close()


def install():
    print("== installing build and stand-in apps into the bottle", flush=True)
    wine("taskkill", "/im", "FakeApp.exe")
    shutil.rmtree(OP_DIR, ignore_errors=True)
    shutil.copytree(os.path.join(ROOT, "artifacts", "win-x64"), OP_DIR)
    runtime = os.path.join(DRIVE_C, "OPFixtures", "_runtime")
    shutil.rmtree(os.path.join(DRIVE_C, "OPFixtures"), ignore_errors=True)
    shutil.copytree(os.path.join(ROOT, "artifacts", "fixture-runtime"), runtime)
    for root in [unix(r"C:\Program Files\FakeChromium"), unix(r"C:\Program Files\FakeElectron"), unix(r"C:\Program Files\FakeFirefox"),
                 unix(r"C:\Program Files\Telegram Desktop"), unix(r"C:\Program Files\FakeCode"), unix(r"C:\Program Files\FakeJetBrains"),
                 unix(r"C:\Program Files\FakePlain"), unix(r"C:\Program Files\Zoom"), os.path.join(LOCAL, "FakeSquirrel")]:
        shutil.rmtree(root, ignore_errors=True)
    for win_exe, markers in FIXTURES.items():
        install_fixture(SQUIRREL_UPDATE if win_exe == "SQUIRREL_UPDATE" else win_exe, markers, runtime)
    # A clean slate: no instances, no shortcuts, no fixture logs from earlier runs.
    shutil.rmtree(os.path.join(LOCAL, "OpenParallel"), ignore_errors=True)
    shutil.rmtree(os.path.join(PROGRAMS, "OpenParallel"), ignore_errors=True)
    shutil.rmtree(FIXTURE_LOGS, ignore_errors=True)
    shutil.rmtree(os.path.join(COMMON_PROGRAMS, "OPFixtures"), ignore_errors=True)


UI_SHORTCUTS = {
    # Start menu name : (target, arguments) for the stand-in apps the UI test picks from
    "Fake Chromium": (r"C:\Program Files\FakeChromium\Application\chrome.exe", ""),
    "Fake Electron": (r"C:\Program Files\FakeElectron\FakeElectron.exe", ""),
    "Fake Firefox": (r"C:\Program Files\FakeFirefox\firefox.exe", ""),
    "Telegram Desktop": (r"C:\Program Files\Telegram Desktop\Telegram.exe", ""),
    "Fake Code": (r"C:\Program Files\FakeCode\Code.exe", ""),
    "Fake PyCharm": (r"C:\Program Files\FakeJetBrains\bin\pycharm64.exe", ""),
    "Plain Tool": (r"C:\Program Files\FakePlain\plain.exe", ""),
    "Zoom": (r"C:\Program Files\Zoom\bin\Zoom.exe", ""),
    "Fake Squirrel": (SQUIRREL_UPDATE, "--processStart FakeSquirrel.exe"),
}


def setup_ui():
    """Installs the build and the stand-in apps, with Start menu shortcuts, for the UI test."""
    install()
    os.makedirs(os.path.join(COMMON_PROGRAMS, "OPFixtures"), exist_ok=True)
    for name, (target, arguments) in UI_SHORTCUTS.items():
        link = "C:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs\\OPFixtures\\" + name + ".lnk"
        opctl("shortcut", link, "--target", target, *(["--args", arguments] if arguments else []))
    apps, _ = opctl("apps")
    print("Start menu apps:", ", ".join(a["name"] for a in apps or []))
    return 0


# MARK: scenarios

def main():
    if "--setup-ui" in sys.argv:
        return setup_ui()
    install()
    s = lambda p: p  # readability

    print("== detection", flush=True)
    expectations = {
        r"C:\Program Files\FakeChromium\Application\chrome.exe": ("Chromium", "Supported", "Plain"),
        r"C:\Program Files\FakeElectron\FakeElectron.exe": ("Electron", "Supported", "Plain"),
        r"C:\Program Files\FakeFirefox\firefox.exe": ("Firefox", "Supported", "Plain"),
        r"C:\Program Files\Telegram Desktop\Telegram.exe": ("Telegram", "Supported", "Plain"),
        r"C:\Program Files\FakeCode\Code.exe": ("VSCode", "Supported", "Plain"),
        r"C:\Program Files\FakeJetBrains\bin\pycharm64.exe": ("JetBrains", "Experimental", "Plain"),
        r"C:\Program Files\FakePlain\plain.exe": ("Generic", "SharesData", "Plain"),
        r"C:\Program Files\Zoom\bin\Zoom.exe": ("Unsupported", "NotSupported", "Plain"),
        "C:\\users\\crossover\\AppData\\Local\\FakeSquirrel\\app-1.0.0\\FakeSquirrel.exe": ("Electron", "Supported", "Squirrel"),
    }
    for exe, (family, status, kind) in expectations.items():
        info, _ = opctl("inspect", exe)
        check(f"inspect {exe.split(chr(92))[-1]} -> {family}/{status}/{kind}",
              info and info["family"] == family and info["status"] == status and info["installKind"] == kind, info)

    print("== installed apps from the Start menu", flush=True)
    os.makedirs(os.path.join(COMMON_PROGRAMS, "OPFixtures"), exist_ok=True)
    opctl("shortcut", r"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\OPFixtures\Fake Electron.lnk",
          "--target", r"C:\Program Files\FakeElectron\FakeElectron.exe")
    opctl("shortcut", r"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\OPFixtures\Fake Squirrel.lnk",
          "--target", SQUIRREL_UPDATE, "--args", "--processStart FakeSquirrel.exe")
    apps, _ = opctl("apps")
    by_name = {a["name"]: a for a in (apps or [])}
    check("apps lists a Start menu app", "Fake Electron" in by_name, list(by_name)[:10])
    check("apps resolves a Squirrel shortcut to the newest version",
          "Fake Squirrel" in by_name and "app-1.1.0" in by_name["Fake Squirrel"]["exePath"], by_name.get("Fake Squirrel"))

    print("== Electron: two instances side by side, each on its own data", flush=True)
    electron = r"C:\Program Files\FakeElectron\FakeElectron.exe"
    a, _ = opctl("create", "--app", electron, "--name", "Work")
    b, _ = opctl("create", "--app", electron, "--name", "Work")
    check("second instance with the same name gets 'Work 2'", a["instanceName"] == "Work" and b["instanceName"] == "Work 2", (a["instanceName"], b["instanceName"]))
    lnk, _ = opctl("read-shortcut", a["shortcutPath"])
    check("Start menu shortcut targets the app with its own --user-data-dir",
          lnk and lnk["target"].lower().endswith("fakeelectron.exe") and "--user-data-dir=" in lnk["arguments"] and r"Instances\work\electron" in lnk["arguments"], lnk)
    check("shortcut file is in Start menu\\Programs\\OpenParallel", os.path.isfile(unix(a["shortcutPath"])), a["shortcutPath"])
    opctl("launch", "work")
    opctl("launch", "work-2")
    started = wait_for(lambda: len(starts_with_profile(r"\Instances\work")) >= 2 and starts_with_profile(r"\Instances\work-2"))
    check("both copies started with their own profile folders",
          starts_with_profile(r"\Instances\work\electron") and starts_with_profile(r"\Instances\work-2\electron"), [r.get("profile") for r in records()])
    running = wait_for(lambda: (lambda r: r if r and "work" in r and "work-2" in r else None)(opctl("running")[0]))
    check("running detects both instances with different processes",
          running and running.get("work") and running.get("work-2") and set(running["work"]) != set(running["work-2"]), running)

    print("== the original app is not mistaken for an instance", flush=True)
    original = subprocess.Popen([WINE, electron], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    wait_for(lambda: [r for r in records() if r.get("event") == "start" and r.get("profile") == "default"])
    running, _ = opctl("running")
    original_pids = [r["pid"] for r in records() if r.get("profile") == "default"]
    flat = {pid for pids in (running or {}).values() for pid in pids}
    check("original app's process belongs to no instance", original_pids and not (set(original_pids) & flat), (original_pids, running))

    print("== opening a running instance again hands off like the real app", flush=True)
    opctl("launch", "work")
    check("second launch on the same profile handed off", wait_for(lambda: [r for r in records() if r.get("event") == "handoff" and r"\instances\work\electron" in (r.get("profile") or "").lower()]))

    print("== trash is refused while running; quit closes only that instance", flush=True)
    _, refused = opctl("trash", "work", ok=False)
    check("trash refused while running", refused.returncode != 0 and "Quit" in refused.stderr, refused.stderr[-200:])
    quit_, _ = opctl("quit", "work")
    check("quit asked the window to close", quit_ and quit_.get("asked"), quit_)
    gone = wait_for(lambda: (lambda r: r if r is not None and "work" not in r else None)(opctl("running")[0]), 20)
    check("work stopped and work-2 kept running", gone is not None and "work-2" in gone, gone)
    check("the stand-in recorded a clean close", [r for r in records() if r.get("event") == "closed"])

    print("== Squirrel: Update.exe starts the newest version with the profile", flush=True)
    sq, _ = opctl("create", "--app", "C:\\users\\crossover\\AppData\\Local\\FakeSquirrel\\app-1.0.0\\FakeSquirrel.exe", "--name", "Squirrel Work")
    check("Squirrel instance launches through Update.exe", sq["launchMode"] == "squirrel" and sq["launchTarget"].lower().endswith("update.exe"), sq["launchTarget"])
    opctl("launch", "squirrel-work")
    sq_start = wait_for(lambda: starts_with_profile(r"\Instances\squirrel-work\electron"))
    check("Update.exe started app-1.1.0 with the instance profile", sq_start and "app-1.1.0" in sq_start[0]["exe"], sq_start)
    check("Squirrel instance detected as running", wait_for(lambda: (opctl("running")[0] or {}).get("squirrel-work")))

    print("== JetBrains: environment-only recipe through the OpenParallel.exe launcher", flush=True)
    jb, _ = opctl("create", "--app", r"C:\Program Files\FakeJetBrains\bin\pycharm64.exe", "--name", "PyCharm Work")
    lnk, _ = opctl("read-shortcut", jb["shortcutPath"])
    check("JetBrains shortcut points at OpenParallel.exe --launch",
          lnk and lnk["target"].lower().endswith("openparallel.exe") and "--launch pycharm-work" in lnk["arguments"], lnk)
    wine(r"C:\OpenParallel\OpenParallel.exe", "--launch", "pycharm-work")
    jb_start = wait_for(lambda: [r for r in records() if r.get("event") == "start" and "PYCHARM_PROPERTIES" in (r.get("environment") or {})])
    check("launcher passed PYCHARM_PROPERTIES to the IDE", jb_start and r"pycharm-work\jetbrains\idea.properties" in jb_start[0]["environment"]["PYCHARM_PROPERTIES"], jb_start)
    check("idea.properties written into the instance", os.path.isfile(os.path.join(INSTANCES, "pycharm-work", "jetbrains", "idea.properties")))
    check("environment-only instance detected as running (process environment read)",
          wait_for(lambda: (opctl("running")[0] or {}).get("pycharm-work")), opctl("running")[0])

    print("== Firefox, Telegram, VS Code recipes", flush=True)
    for exe, slug, needle in [
        (r"C:\Program Files\FakeFirefox\firefox.exe", "fox", r"\Instances\fox\firefox"),
        (r"C:\Program Files\Telegram Desktop\Telegram.exe", "tg", r"\Instances\tg\telegram"),
        (r"C:\Program Files\FakeCode\Code.exe", "code", r"\Instances\code\vscode\user-data"),
    ]:
        opctl("create", "--app", exe, "--name", slug)
        opctl("launch", slug)
        check(f"{slug}: started on its own profile", wait_for(lambda: starts_with_profile(needle)), needle)

    print("== website instance: browser in app mode with its own data", flush=True)
    web, _ = opctl("create", "--web", "https://example.com/", "--name", "Example", "--browser", r"C:\Program Files\FakeChromium\Application\chrome.exe")
    opctl("launch", "example")
    web_start = wait_for(lambda: starts_with_profile(r"\Instances\example\web"))
    check("website opened with --app and its own user-data-dir", web_start and "--app=https://example.com/" in web_start[0]["args"], web_start)

    print("== refusals", flush=True)
    _, zoom = opctl("create", "--app", r"C:\Program Files\Zoom\bin\Zoom.exe", ok=False)
    check("Zoom refused with the website alternative", zoom.returncode != 0 and "only one running copy" in zoom.stderr, zoom.stderr[-200:])
    plain, _ = opctl("create", "--app", r"C:\Program Files\FakePlain\plain.exe", "--name", "Plain")
    check("generic app created without profile switches", plain and not plain["profileIsolation"] and plain["launchArguments"] == [], plain)

    print("== quit everything and move it all to the Recycle Bin", flush=True)
    listed, _ = opctl("list")
    for m in listed:
        opctl("quit", m["instanceSlug"])
    wait_for(lambda: not (opctl("running")[0] or {}), 30)
    leftover = opctl("running")[0]
    check("everything quit", not leftover, leftover)
    for m in listed:
        opctl("trash", m["instanceSlug"], ok=False)
    remaining, _ = opctl("list")
    check("no instances left after Move to Recycle Bin", remaining == [], remaining)
    check("instance folders removed", not os.listdir(INSTANCES) if os.path.isdir(INSTANCES) else True, os.listdir(INSTANCES) if os.path.isdir(INSTANCES) else None)
    shortcuts_dir = os.path.join(PROGRAMS, "OpenParallel")
    check("Start menu shortcuts removed", not os.path.isdir(shortcuts_dir) or not [f for f in os.listdir(shortcuts_dir) if f.endswith(".lnk")],
          os.listdir(shortcuts_dir) if os.path.isdir(shortcuts_dir) else None)

    original.terminate()
    wine("taskkill", "/im", "FakeApp.exe")
    passed = sum(1 for _, ok, _ in results if ok)
    print(f"\n{passed}/{len(results)} checks passed", flush=True)
    return 0 if passed == len(results) else 1


if __name__ == "__main__":
    sys.exit(main())
