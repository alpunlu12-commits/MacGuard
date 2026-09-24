#!/bin/bash
# MacGuard.app paketini sıfırdan üretir.
#   ./Scripts/build_app.sh            -> build/MacGuard.app
#   ./Scripts/build_app.sh --install  -> /Applications altına da kopyalar
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

APP_NAME="MacGuard"
BUNDLE_ID="com.alpunlu.macguard"
OUT="build/${APP_NAME}.app"

echo "▸ Swift derleniyor (release)…"
swift build -c release

echo "▸ Paket hazırlanıyor…"
rm -rf "$OUT"
mkdir -p "$OUT/Contents/MacOS" "$OUT/Contents/Resources"

cp ".build/release/${APP_NAME}" "$OUT/Contents/MacOS/${APP_NAME}"
cp "Resources/Info.plist"       "$OUT/Contents/Info.plist"
printf 'APPL????' > "$OUT/Contents/PkgInfo"

echo "▸ İkon üretiliyor…"
ICONSET="build/AppIcon.iconset"
rm -rf "$ICONSET"; mkdir -p "$ICONSET"
if swift Scripts/make_icon.swift "build/icon-1024.png" >/dev/null 2>&1; then
  for spec in "16 16x16" "32 16x16@2x" "32 32x32" "64 32x32@2x" \
              "128 128x128" "256 128x128@2x" "256 256x256" \
              "512 256x256@2x" "512 512x512" "1024 512x512@2x"; do
    set -- $spec
    sips -z "$1" "$1" "build/icon-1024.png" --out "$ICONSET/icon_$2.png" >/dev/null 2>&1
  done
  iconutil -c icns "$ICONSET" -o "$OUT/Contents/Resources/AppIcon.icns" 2>/dev/null \
    && echo "  ikon eklendi" || echo "  ikon atlandı (zararsız)"
else
  echo "  ikon üretilemedi, varsayılanla devam ediliyor (zararsız)"
fi
rm -rf "$ICONSET" build/icon-1024.png

echo "▸ İmzalanıyor (ad-hoc)…"
codesign --force --deep --sign - --identifier "$BUNDLE_ID" "$OUT" 2>/dev/null \
  && echo "  imzalandı" || echo "  imza atlandı"

# Launch Services'in eski sürümü hatırlamaması için kaydı tazele.
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister \
  -f "$OUT" >/dev/null 2>&1 || true

echo ""
echo "✅ Hazır: $ROOT/$OUT"

if [[ "${1:-}" == "--install" ]]; then
  echo "▸ /Applications içine kopyalanıyor…"
  rm -rf "/Applications/${APP_NAME}.app"
  cp -R "$OUT" "/Applications/${APP_NAME}.app"
  echo "✅ Kuruldu: /Applications/${APP_NAME}.app"
fi

echo ""
echo "Çalıştırmak için:  open \"$ROOT/$OUT\""
