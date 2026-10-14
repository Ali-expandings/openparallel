#!/usr/bin/env swift

import AppKit

guard CommandLine.arguments.count == 2 else {
    FileHandle.standardError.write(Data("usage: generate-icon.swift <output.png>\n".utf8))
    exit(64)
}

let outputURL = URL(fileURLWithPath: CommandLine.arguments[1])
let size = NSSize(width: 1024, height: 1024)
let image = NSImage(size: size)

image.lockFocus()
guard let context = NSGraphicsContext.current?.cgContext else {
    FileHandle.standardError.write(Data("Could not create icon graphics context.\n".utf8))
    exit(1)
}

context.setAllowsAntialiasing(true)
context.setShouldAntialias(true)

let canvas = NSRect(x: 56, y: 56, width: 912, height: 912)
let background = NSBezierPath(roundedRect: canvas, xRadius: 214, yRadius: 214)
let gradient = NSGradient(colors: [
    NSColor(calibratedRed: 0.055, green: 0.067, blue: 0.086, alpha: 1),
    NSColor(calibratedRed: 0.105, green: 0.125, blue: 0.165, alpha: 1)
])!
gradient.draw(in: background, angle: -42)

let back = NSBezierPath(roundedRect: NSRect(x: 238, y: 292, width: 390, height: 390), xRadius: 92, yRadius: 92)
back.lineWidth = 34
NSColor(calibratedWhite: 0.82, alpha: 0.75).setStroke()
back.stroke()

NSGraphicsContext.saveGraphicsState()
let shadow = NSShadow()
shadow.shadowColor = NSColor.black.withAlphaComponent(0.36)
shadow.shadowBlurRadius = 42
shadow.shadowOffset = NSSize(width: 0, height: -18)
shadow.set()

let front = NSBezierPath(roundedRect: NSRect(x: 400, y: 346, width: 390, height: 390), xRadius: 92, yRadius: 92)
NSColor(calibratedRed: 0.91, green: 0.59, blue: 0.22, alpha: 1).setFill()
front.fill()

NSGraphicsContext.restoreGraphicsState()
image.unlockFocus()

guard
    let tiff = image.tiffRepresentation,
    let bitmap = NSBitmapImageRep(data: tiff),
    let png = bitmap.representation(using: .png, properties: [:])
else {
    FileHandle.standardError.write(Data("Could not encode icon PNG.\n".utf8))
    exit(1)
}

try png.write(to: outputURL, options: .atomic)
