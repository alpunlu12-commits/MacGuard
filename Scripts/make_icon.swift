// MacGuard uygulama ikonunu çizer. Kullanım: swift Scripts/make_icon.swift <çıktı.png>
import AppKit

let outPath = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "icon.png"
let size = 1024.0

let image = NSImage(size: NSSize(width: size, height: size))
image.lockFocus()

// Koyu, hafif gradyanlı yuvarlak kare zemin
let rect = NSRect(x: 0, y: 0, width: size, height: size)
let path = NSBezierPath(roundedRect: rect, xRadius: size * 0.22, yRadius: size * 0.22)
NSGradient(colors: [
    NSColor(calibratedRed: 0.09, green: 0.13, blue: 0.18, alpha: 1),
    NSColor(calibratedRed: 0.03, green: 0.05, blue: 0.07, alpha: 1)
])?.draw(in: path, angle: -90)

// Ortada yeşil kalkan
let config = NSImage.SymbolConfiguration(pointSize: size * 0.52, weight: .semibold)
if let symbol = NSImage(systemSymbolName: "lock.shield.fill", accessibilityDescription: nil)?
    .withSymbolConfiguration(config) {
    let tinted = NSImage(size: symbol.size)
    tinted.lockFocus()
    NSColor(calibratedRed: 0.20, green: 0.83, blue: 0.60, alpha: 1).set()
    NSRect(origin: .zero, size: symbol.size).fill(using: .sourceOver)
    symbol.draw(at: .zero, from: .zero, operation: .destinationIn, fraction: 1)
    tinted.unlockFocus()

    let target = NSRect(x: (size - symbol.size.width) / 2,
                        y: (size - symbol.size.height) / 2,
                        width: symbol.size.width,
                        height: symbol.size.height)
    tinted.draw(in: target)
}

image.unlockFocus()

guard let tiff = image.tiffRepresentation,
      let bitmap = NSBitmapImageRep(data: tiff),
      let png = bitmap.representation(using: .png, properties: [:]) else {
    FileHandle.standardError.write(Data("ikon üretilemedi\n".utf8))
    exit(1)
}
try? png.write(to: URL(fileURLWithPath: outPath))
print("ikon yazıldı: \(outPath)")
