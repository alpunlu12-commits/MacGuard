#!/bin/bash
# MacGuard kurulum betiği.
#
# Ne yapar:
#   1. macOS sürümünü ve derleyiciyi kontrol eder
#   2. Kaynaktan derler
#   3. /Applications altına kurar
#   4. Uygulamayı açar
#
# Çalıştırmadan önce bu dosyayı okuyabilirsin — kısa tutuldu, hepsi burada.
set -euo pipefail

BOLD=$'\033[1m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'; RED=$'\033[31m'; OFF=$'\033[0m'
say()  { printf "%s\n" "$1"; }
ok()   { printf "${GREEN}✓${OFF} %s\n" "$1"; }
warn() { printf "${YELLOW}!${OFF} %s\n" "$1"; }
die()  { printf "${RED}✗${OFF} %s\n" "$1" >&2; exit 1; }

printf "\n${BOLD}MacGuard kurulumu${OFF}\n\n"

# --- 1. macOS sürümü ---
MAJOR=$(sw_vers -productVersion | cut -d. -f1)
if [ "$MAJOR" -lt 14 ]; then
  die "macOS 14 (Sonoma) veya üstü gerekiyor. Sende: $(sw_vers -productVersion)"
fi
ok "macOS $(sw_vers -productVersion)"

# --- 2. Derleyici ---
if ! xcode-select -p >/dev/null 2>&1; then
  warn "Xcode Command Line Tools kurulu değil."
  say ""
  say "  Şu komutu çalıştır, açılan pencerede 'Yükle' de:"
  say "    ${BOLD}xcode-select --install${OFF}"
  say ""
  say "  Kurulum bitince bu betiği tekrar çalıştır."
  exit 1
fi
if ! command -v swift >/dev/null 2>&1; then
  die "swift bulunamadı. 'xcode-select --install' ile Command Line Tools kur."
fi
ok "Swift $(swift --version 2>/dev/null | head -1 | grep -o 'version [0-9.]*' | cut -d' ' -f2)"

# --- 3. Derle ---
cd "$(dirname "${BASH_SOURCE[0]}")"
say ""
say "Derleniyor… (ilk seferde bir dakika sürebilir)"
if ! ./Scripts/build_app.sh >/tmp/macguard-build.log 2>&1; then
  say ""
  tail -20 /tmp/macguard-build.log
  die "Derleme başarısız. Tam kayıt: /tmp/macguard-build.log"
fi
ok "Derlendi"

# --- 4. Kur ---
# Çalışan bir kopya varsa kapat. Aynı bundle kimliğiyle 'open' çağırmak,
# yeni kopyayı açmak yerine eskisine odaklanır; kurulum yapılmış gibi görünür
# ama kullanıcı hâlâ eski sürümü kullanıyor olur.
if pgrep -x MacGuard >/dev/null 2>&1; then
  warn "Çalışan MacGuard kapatılıyor"
  pkill -x MacGuard 2>/dev/null || true
  sleep 1
fi

if [ -d "/Applications/MacGuard.app" ]; then
  warn "Eski sürüm siliniyor"
  rm -rf "/Applications/MacGuard.app"
fi
cp -R "build/MacGuard.app" "/Applications/MacGuard.app"
# Kendi derlediğimiz için karantina damgası yok, yine de garanti olsun.
xattr -dr com.apple.quarantine "/Applications/MacGuard.app" 2>/dev/null || true
ok "Kuruldu: /Applications/MacGuard.app"

# --- 5. Aç ---
say ""
open "/Applications/MacGuard.app"
printf "${BOLD}Hazır.${OFF} Uygulama açılıyor.\n\n"
say "Sırada:"
say "  1. PIN belirle — alarmı durdurabilen tek şey bu"
say "  2. 'İzin iste' ile kamera iznini ver"
say "  3. 'Kamerayı Ayarla' ile eşikleri kendi ortamına göre ayarla"
say ""
