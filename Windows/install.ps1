# MacGuard (Windows) kurulum betiği.
#
# Ne yapar:
#   1. Windows sürümünü ve .NET SDK'yı kontrol eder
#   2. Kaynaktan derler (tek dosyalık MacGuard.exe)
#   3. %LOCALAPPDATA%\Programs\MacGuard altına kurar, Başlat menüsüne kısayol ekler
#   4. Uygulamayı açar
#
# Yönetici yetkisi istemez. Çalıştırmadan önce bu dosyayı okuyabilirsin —
# kısa tutuldu, hepsi burada.
#
# Kullanım (depo kökünden):
#   powershell -ExecutionPolicy Bypass -File Windows\install.ps1
# Kaldırmak için:
#   powershell -ExecutionPolicy Bypass -File Windows\install.ps1 -Uninstall

param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'

function Ok($m)   { Write-Host "  √ $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  ! $m" -ForegroundColor Yellow }
function Die($m)  { Write-Host "  x $m" -ForegroundColor Red; exit 1 }

$dest     = Join-Path $env:LOCALAPPDATA 'Programs\MacGuard'
$exe      = Join-Path $dest 'MacGuard.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'MacGuard.lnk'
$runKey   = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

Write-Host ''
Write-Host 'MacGuard (Windows) kurulumu' -ForegroundColor White
Write-Host ''

# Çalışan bir kopya varsa dosya kilitli olur.
if (Get-Process -Name 'MacGuard' -ErrorAction SilentlyContinue) {
    Die 'MacGuard çalışıyor. Önce korumayı kapat, sonra bildirim alanındaki kalkan simgesinden "Çık" de.'
}

if ($Uninstall) {
    Remove-Item $exe -Force -ErrorAction SilentlyContinue
    Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $shortcut -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $runKey -Name 'MacGuard' -ErrorAction SilentlyContinue
    Ok 'Uygulama, kısayol ve başlangıç kaydı kaldırıldı.'
    Write-Host ''
    Write-Host "  PIN, ayarlar, olay kaydı ve fotoğraflar duruyor: $env:APPDATA\MacGuard"
    Write-Host '  Onları da silmek istersen klasörü elle sil.'
    exit 0
}

# --- 1. Windows sürümü ---
$build = [Environment]::OSVersion.Version.Build
if ($build -lt 19041) {
    Die "Windows 10 sürüm 2004 (derleme 19041) veya üstü gerekiyor. Sende: derleme $build"
}
Ok "Windows derleme $build"

# --- 2. .NET SDK ---
$hasSdk = $false
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    foreach ($line in (& dotnet --list-sdks)) {
        if ($line -match '^(\d+)\.' -and [int]$Matches[1] -ge 10) { $hasSdk = $true }
    }
}
if (-not $hasSdk) {
    Warn '.NET 10 SDK kurulu değil.'
    Write-Host ''
    Write-Host '  Şu komutu çalıştır, kurulum bitince yeni bir PowerShell penceresinde bu betiği tekrar çalıştır:'
    Write-Host '    winget install Microsoft.DotNet.SDK.10' -ForegroundColor White
    Write-Host ''
    exit 1
}
Ok '.NET SDK bulundu'

# --- 3. Derle ---
$rid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$out = Join-Path $PSScriptRoot "bin\publish\$rid"
Write-Host "  Derleniyor ($rid)… ilk seferde paketler indirildiği için birkaç dakika sürebilir."
& dotnet publish (Join-Path $PSScriptRoot 'MacGuard.Windows.csproj') -c Release -r $rid -o $out --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Die 'Derleme başarısız. Yukarıdaki hata mesajına bak.' }
Ok 'Derlendi'

# --- 4. Kur ---
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $out 'MacGuard.exe') $exe -Force
Ok "Kuruldu: $exe"

$ws = New-Object -ComObject WScript.Shell
$lnk = $ws.CreateShortcut($shortcut)
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $dest
$lnk.Description = 'MacGuard — dizüstü bilgisayar için hırsızlık alarmı'
$lnk.Save()
Ok 'Başlat menüsüne kısayol eklendi'

# --- 5. Aç ---
Start-Process $exe
Write-Host ''
Ok 'MacGuard açıldı. İlk iş bir PIN belirle.'
Write-Host ''
