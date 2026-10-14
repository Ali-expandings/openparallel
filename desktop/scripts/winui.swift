// Drives a Wine window on macOS for the UI tests: finds it, screenshots it, and sends keys
// and clicks to that one process only. Every input is posted to the target PID, and only
// after the target's window is confirmed frontmost, so nothing can land in another app.
//
//   <title> is the window title or the owning process name, e.g. OpenParallel.exe
//   winui find <title>                     -> "<pid> <windowid> <x> <y> <w> <h>" of the window
//   winui front <title>                    -> bring it forward (no input sent)
//   winui shot <title> <out.png>           -> screenshot of that window
//   winui key <title> <key> [ctrl|shift|alt]...
//   winui type <title> <text>
//   winui scroll <title> <x> <y> <lines>        (negative scrolls down)
//   winui click <title> <x> <y> [double|right]   (points, relative to the window's top-left)
import AppKit
import CoreGraphics
import Foundation

struct Target { let pid: pid_t; let id: CGWindowID; let frame: CGRect }

func windows() -> [[String: Any]] {
    CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
}

func find(_ title: String) -> Target? {
    for w in windows() where (w[kCGWindowLayer as String] as? Int) == 0 {
        // Titles need Screen Recording permission to be listed; the owning process name
        // (the Windows exe name under Wine) works without it.
        let name = w[kCGWindowName as String] as? String ?? ""
        let owner = w[kCGWindowOwnerName as String] as? String ?? ""
        guard name == title || owner == title else { continue }
        let pid = pid_t(w[kCGWindowOwnerPID as String] as? Int ?? 0)
        let id = CGWindowID(w[kCGWindowNumber as String] as? Int ?? 0)
        var frame = CGRect.zero
        if let bounds = w[kCGWindowBounds as String] as? NSDictionary { CGRectMakeWithDictionaryRepresentation(bounds, &frame) }
        return Target(pid: pid, id: id, frame: frame)
    }
    return nil
}

func fail(_ message: String) -> Never {
    FileHandle.standardError.write((message + "\n").data(using: .utf8)!)
    exit(1)
}

/// Brings the target forward and confirms its window is the topmost normal window.
func focus(_ target: Target) {
    NSRunningApplication(processIdentifier: target.pid)?.activate(options: [.activateAllWindows, .activateIgnoringOtherApps])
    for attempt in 0..<30 {
        if attempt == 5 {
            // Some Wine builds ignore activate(); System Events can still raise the process.
            let script = Process()
            script.executableURL = URL(fileURLWithPath: "/usr/bin/osascript")
            script.arguments = ["-e", "tell application \"System Events\" to set frontmost of (first process whose unix id is \(target.pid)) to true"]
            try? script.run()
            script.waitUntilExit()
        }
        Thread.sleep(forTimeInterval: 0.1)
        if let top = windows().first(where: { ($0[kCGWindowLayer as String] as? Int) == 0 }),
           pid_t(top[kCGWindowOwnerPID as String] as? Int ?? 0) == target.pid {
            return
        }
    }
    fail("ABORT: target window is not frontmost")
}

let keyCodes: [String: CGKeyCode] = [
    "a": 0, "s": 1, "d": 2, "f": 3, "h": 4, "g": 5, "z": 6, "x": 7, "c": 8, "v": 9, "b": 11, "q": 12,
    "w": 13, "e": 14, "r": 15, "y": 16, "t": 17, "1": 18, "2": 19, "3": 20, "4": 21, "6": 22, "5": 23,
    "9": 25, "7": 26, "8": 28, "0": 29, "o": 31, "u": 32, "i": 34, "p": 35, "l": 37, "j": 38, "k": 40,
    "n": 45, "m": 46, ",": 43, "return": 36, "enter": 36, "tab": 48, "space": 49, "backspace": 51,
    "esc": 53, "escape": 53, "delete": 117, "f1": 122, "f5": 96, "left": 123, "right": 124, "down": 125, "up": 126,
    "home": 115, "end": 119, ".": 47, "/": 44, ";": 41, "-": 27, "=": 24, "\\": 42,
]

func post(_ target: Target, key: CGKeyCode, flags: CGEventFlags) {
    let source = CGEventSource(stateID: .hidSystemState)
    for down in [true, false] {
        guard let event = CGEvent(keyboardEventSource: source, virtualKey: key, keyDown: down) else { continue }
        event.flags = flags
        event.postToPid(target.pid)
        Thread.sleep(forTimeInterval: 0.03)
    }
}

let args = Array(CommandLine.arguments.dropFirst())
guard args.count >= 2 else { fail("usage: winui find|shot|key|type|click <title> ...") }
guard let target = find(args[1]) else { fail("no window titled \(args[1])") }

switch args[0] {
case "find":
    print("\(target.pid) \(target.id) \(Int(target.frame.minX)) \(Int(target.frame.minY)) \(Int(target.frame.width)) \(Int(target.frame.height))")
case "front":
    focus(target)
    print("front")
case "shot":
    let task = Process()
    task.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
    task.arguments = ["-x", "-o", "-l", String(target.id), args[2]]
    try task.run()
    task.waitUntilExit()
    exit(task.terminationStatus)
case "key":
    var flags: CGEventFlags = []
    for mod in args.dropFirst(3) {
        switch mod {
        // Wine maps the Mac Command key to Windows Ctrl only when configured; Control is Ctrl.
        case "ctrl": flags.insert(.maskControl)
        case "shift": flags.insert(.maskShift)
        case "alt": flags.insert(.maskAlternate)
        default: fail("unknown modifier \(mod)")
        }
    }
    guard let code = keyCodes[args[2].lowercased()] else { fail("unknown key \(args[2])") }
    focus(target)
    post(target, key: code, flags: flags)
    print("sent \(args[2])")
case "type":
    focus(target)
    for ch in args[2] {
        let shifted: [Character: String] = [":": ";", "_": "-", "+": "="]
        let text = shifted[ch] ?? String(ch).lowercased()
        guard let code = keyCodes[text == " " ? "space" : text] else { fail("cannot type \(ch)") }
        let flags: CGEventFlags = ch.isUppercase || shifted[ch] != nil ? .maskShift : []
        post(target, key: code, flags: flags)
    }
    print("typed \(args[2].count) characters")
case "click":
    guard args.count >= 4, let x = Double(args[2]), let y = Double(args[3]) else { fail("click needs x y") }
    let kind = args.count > 4 ? args[4] : "single"
    focus(target)
    let point = CGPoint(x: target.frame.minX + x, y: target.frame.minY + y)
    // Only click when the topmost window under the point belongs to the target.
    let under = windows().first { w in
        guard (w[kCGWindowLayer as String] as? Int) == 0, let b = w[kCGWindowBounds as String] as? NSDictionary else { return false }
        var r = CGRect.zero
        CGRectMakeWithDictionaryRepresentation(b, &r)
        return r.contains(point)
    }
    guard let under, pid_t(under[kCGWindowOwnerPID as String] as? Int ?? 0) == target.pid else { fail("ABORT: point is not over the target window") }
    let right = kind == "right"
    let downType: CGEventType = right ? .rightMouseDown : .leftMouseDown
    let upType: CGEventType = right ? .rightMouseUp : .leftMouseUp
    let source = CGEventSource(stateID: .hidSystemState)
    CGEvent(mouseEventSource: source, mouseType: .mouseMoved, mouseCursorPosition: point, mouseButton: .left)?.post(tap: .cghidEventTap)
    Thread.sleep(forTimeInterval: 0.08)
    for clickState in (kind == "double" ? [1, 2] : [1]) {
        for type in [downType, upType] {
            let event = CGEvent(mouseEventSource: source, mouseType: type, mouseCursorPosition: point, mouseButton: right ? .right : .left)
            event?.setIntegerValueField(.mouseEventClickState, value: Int64(clickState))
            event?.post(tap: .cghidEventTap)
            Thread.sleep(forTimeInterval: 0.05)
        }
    }
    print("clicked \(Int(x)),\(Int(y))")
case "scroll":
    // winui scroll <title> <x> <y> <lines>   (negative lines scroll down)
    guard args.count >= 5, let x = Double(args[2]), let y = Double(args[3]), let lines = Int32(args[4]) else { fail("scroll needs x y lines") }
    focus(target)
    let point = CGPoint(x: target.frame.minX + x, y: target.frame.minY + y)
    let under = windows().first { w in
        guard (w[kCGWindowLayer as String] as? Int) == 0, let b = w[kCGWindowBounds as String] as? NSDictionary else { return false }
        var r = CGRect.zero
        CGRectMakeWithDictionaryRepresentation(b, &r)
        return r.contains(point)
    }
    guard let under, pid_t(under[kCGWindowOwnerPID as String] as? Int ?? 0) == target.pid else { fail("ABORT: point is not over the target window") }
    CGEvent(mouseEventSource: nil, mouseType: .mouseMoved, mouseCursorPosition: point, mouseButton: .left)?.post(tap: .cghidEventTap)
    Thread.sleep(forTimeInterval: 0.08)
    for _ in 0..<abs(lines) {
        CGEvent(scrollWheelEvent2Source: nil, units: .line, wheelCount: 1, wheel1: lines < 0 ? -3 : 3, wheel2: 0, wheel3: 0)?.post(tap: .cghidEventTap)
        Thread.sleep(forTimeInterval: 0.03)
    }
    print("scrolled \(lines)")
default:
    fail("unknown command \(args[0])")
}
