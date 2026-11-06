# MacGuard — Windows sürümü

Kafede masada bıraktığın dizüstü bilgisayar için hırsızlık alarmı, artık Windows'ta da var.
Koruma modunu başlatırsın; bilgisayarına dokunan olursa siren çalar ve ancak senin PIN'inle susar.

Windows 10 (2004+) / Windows 11 · x64 ve ARM64 · C# + WPF (.NET 10) · tek bağımlılık: [NAudio](https://github.com/naudio/NAudio) (MIT)

macOS sürümüyle aynı davranış, aynı Türkçe arayüz, aynı eşikler. Hareket algılama, sahne
sakinleşmesi, PIN saklama ve siren sesi birebir aynı algoritmayla çalışır. Platform
farklarını aşağıda tek tek yazdım.

> ⚠️ **Durum: askıda.** Hazır paket (`MacGuard.exe`) ciddi hatalar çıkardı ve 6 Kasım 2026'da
> Releases'ten kaldırıldı. Düzeltilene kadar indirilebilir bir Windows sürümü yok; indirdiysen
> şimdilik kullanma. Kod burada duruyor, derlemesi her push'ta GitHub Actions'ta doğrulanıyor.
> Bir sorun görürsen [issue aç](https://github.com/alpunlu12-commits/MacGuard/issues).

---

## Kurulum

### 1. Hazır paketi indir — şimdilik yok

Hazır paket askıya alındı (yukarıdaki Durum notu). Düzelince yeni bir sürümle
[Releases sayfasına](https://github.com/alpunlu12-commits/MacGuard/releases/latest) dönecek;
o zaman bilgisayarına uygun zip'i indireceksin:

| Dosya | Hangi bilgisayar |
|---|---|
| `MacGuard-Windows-win-x64.zip` | Çoğu PC (Intel / AMD işlemci) |
| `MacGuard-Windows-win-arm64.zip` | ARM işlemcili bilgisayarlar (ör. Snapdragon) |

Emin değilsen: **Ayarlar → Sistem → Hakkında → Sistem türü** "x64 tabanlı işlemci" diyorsa
x64, "ARM tabanlı işlemci" diyorsa arm64. Zip'i aç, `MacGuard.exe`'yi istediğin bir klasöre koy ve çalıştır. Kurulum gerekmez,
.NET kurman da gerekmez.

İlk açılışta Windows **SmartScreen** uyaracak: *"Windows bilgisayarınızı korudu"*.
Bu bir hata değil; uygulama kod imzalı değil (sertifika yıllık ücret istiyor). Açmak için:

> **Ek bilgi** → **Yine de çalıştır**

### 2. Kaynaktan derle (önerilen)

Bir güvenlik aracını başkasının derlediği hâliyle çalıştırmak zorunda değilsin.
[.NET 10 SDK](https://dotnet.microsoft.com/download) gerekir:

```powershell
winget install Microsoft.DotNet.SDK.10
```

Sonra:

```powershell
git clone https://github.com/alpunlu12-commits/MacGuard.git
cd MacGuard
powershell -ExecutionPolicy Bypass -File Windows\install.ps1
```

Betik Windows sürümünü ve SDK'yı kontrol eder, derler, `%LOCALAPPDATA%\Programs\MacGuard`
altına kurar, Başlat menüsüne kısayol ekler ve uygulamayı açar. Yönetici yetkisi istemez.
Kaldırmak için aynı komutu `-Uninstall` ile çalıştır.

---

## İlk açılışta yapılacaklar

1. **PIN belirle.** Uygulama ilk açılışta sorar. Alarmı durdurabilen tek şey budur.
   PIN düz metin olarak hiçbir yere yazılmaz: 50.000 tur tekrarlanmış, tuzlanmış SHA-256
   özeti olarak `%APPDATA%\MacGuard\pin.json` içinde durur. Bu klasör yalnızca senin
   Windows kullanıcına açıktır.
2. **Kamera erişimini kontrol et.** Windows'ta masaüstü uygulamaları için izin penceresi
   çıkmaz; tek bir anahtar var:
   > **Ayarlar → Gizlilik ve güvenlik → Kamera** → *"Masaüstü uygulamalarının kameranıza
   > erişmesine izin ver"* açık olmalı.

   Kapalıysa panelde sarı bir uyarı çıkar ve tek tıkla o sayfayı açar.
3. **Kamerayı ayarla.** Korumayı başlatmadan `Kamerayı Ayarla` ile canlı ölçümü aç.
   İki çubuk birden eşiği geçmeli:
   - bilgisayarı **yerinden oynat** → her iki çubuk da sarı çizgiyi aşmalı
   - **elini kameranın önünde salla** → "kareye yayılma" çubuğu eşiğin altında kalmalı

   İkincisi eşiği aşıyorsa Ayarlar > Hassasiyet > **Kare kaplama eşiği**'ni yükselt.

Ana pencereyi kapatmak uygulamayı kapatmaz. MacGuard bildirim alanında (saatin yanındaki
kalkan simgesi) çalışmaya devam eder. Simgenin rengi durumu gösterir: gri kapalı, sarı geri
sayım, yeşil koruma, kırmızı alarm. Sağ tıkla korumayı başlatabilir ya da çıkabilirsin.

---

## Tetikleyiciler — Windows'ta nasıl çalışıyor

| Tetik | Windows'ta |
|---|---|
| **Hareket** | Kamera kareleri karşılaştırılır (`MediaCapture`); değişim *tüm kareye yayılmışsa* bilgisayar oynatılmış demektir |
| **Yakınlık** | Windows'un yerleşik yüz algılayıcısı (`FaceDetector`) yüz kutusunu ölçer; kadrajda fazla büyürse biri çok yaklaşmış demektir |
| **Şarj kablosu** | Güç kaynağı bildirimi (`GUID_ACDC_POWER_SOURCE`) — adaptör çıkarıldığı anda |
| **Ekran kapağı** | Kapak bildirimi (`GUID_LIDSWITCH_STATE_CHANGE`) ve uykuya geçiş bildirimi |
| **USB aygıt** | Aygıt bildirimleri (`WM_DEVICECHANGE`) — bellek, kablo, fare takılır/çıkarılır |
| **Klavye / touchpad** | "Son girdiden beri geçen süre" sayacı (`GetLastInputInfo`) — kanca ya da izin gerektirmez |
| **Harici ekran** | Dock / monitör bağlantısı koparsa |

Kare kaplama, sahne sakinleşmesi ve "iki ardışık kare" kuralı macOS sürümüyle aynı. Ayrıntılı
açıklama ana [README](../README.md#tetikleyiciler)'de.

---

## Alarm çaldığında

- Alarm sesinin çalacağı aygıtın ses seviyesi ayarladığın seviyeye çıkar, sessizdeyse açılır
- **Saniyede bir kontrol edilir:** biri ses tuşuyla kısarsa, sessize alırsa ya da **Ses
  karıştırıcısında yalnızca MacGuard'ı kısarsa** anında geri getirilir
- Siren doğrudan **bilgisayarın hoparlörüne** çalınır; kulaklık varsayılan aygıt olsa bile
- Siren çalar (kod içinde üretilir, ses dosyası taşınmaz), Türkçe sesli uyarı okunur
  (Windows'ta Türkçe ses yoksa İngilizce okunur)
- Tüm ekranları kırmızı bir perde kaplar, **her ekranda** PIN tuş takımı
- **Win tuşu, Alt+Tab, Alt+Esc, Ctrl+Esc, Ctrl+Shift+Esc ve Alt+F4 devre dışı kalır**
- Kameradan davetsiz misafirin fotoğrafı çekilir (`%APPDATA%\MacGuard\Snapshots`)
- Açıksa telefonuna bildirim + fotoğraf gider (ntfy — kurulumu ana README'de)

Koruma açıkken uygulama kapatılamaz; Windows'un oturumu kapatma ya da yeniden başlatma
isteği de reddedilir. Windows bu durumda *"MacGuard kapatmayı engelliyor"* der.

Perde nöbetçisi macOS'takiyle aynı: ekran düzeni değişirse perde yeniden kurulur, klavyeye
ya da touchpad'e dokunulduğunda perde öne alınır ve klavye farenin bulunduğu ekrandaki
tuş takımına verilir.

### Kapak kapanınca alarm susmasın

Normalde kapak kapanınca Windows uyur. Ayarlar > Alarm > **"Kapak kapansa bile uyuma"**
koruma süresince güç planındaki *"Kapağı kapattığımda"* ayarını hem prizde hem pilde
**"Hiçbir şey yapma"**ya çeker, koruma kapanınca eski değerlerine döndürür.

macOS'taki gibi bir sudoers kuralına gerek yok: Windows bu ayarı çoğu bilgisayarda yönetici
yetkisi olmadan değiştirmeye izin veriyor. Grup ilkesiyle kilitlenmişse bir kez UAC onayı
sorulur.

Seçenek kapalıyken de kapak kapanması yakalanır ve alarm başlar. Sistem uyursa ses
kesilir ama **bilgisayar uyanır uyanmaz siren kaldığı yerden devam eder.**

Uygulama bu ayar açıkken çökerse, eski değerler önceden `%APPDATA%\MacGuard\lid-leftover.json`
dosyasına yazılmış olur ve bir sonraki açılışta otomatik geri yüklenir.

---

## Sınırlar — dürüst liste

- **Ctrl+Alt+Del engellenemez.** Windows'un güvenli dikkat dizisidir; hiçbir uygulama onu
  durduramaz. Oradan açılan **Görev Yöneticisi MacGuard'ı sonlandırabilir** ve siren susar.
  Bu, macOS sürümüne göre en büyük fark. Fotoğraf ve telefon bildirimi alarmın ilk
  saniyesinde gittiği için kanıt yine de kalır.
- **Win+L ekranı kilitler** ama siren çalmaya devam eder.
- **Güç düğmesine uzun basmak her şeyi durdurur.** Yazılımla engellenebilecek bir şey değil.
- **Kamera ışığı yanar.** Donanım seviyesinde; kapatılamaz ve kapatılmamalı.
- **Kapak kapalıyken kamera kör olur.** Diğer sensörler çalışmaya devam eder.
- **Dahili hoparlör seçimi en iyi çabadır.** Bazı ses kartları kulaklık takılınca hoparlörü
  tamamen kapatır; o durumda ses kulaklıktan çıkar.
- **İkili imzasız.** SmartScreen ilk açılışta uyarır. Kaynaktan derlemek bu yüzden önerilir.
- **Bu bir caydırıcıdır, kilit değildir.** Gürültü çıkarır ve delil toplar; bilgisayarın
  çalınmasını fiziksel olarak engellemez.

---

## ⚠️ Acil durum: PIN'i unuttum / uygulama takıldı

Alarm çalarken kaçış kısayolları kapalıdır. Kurtarma yolları:

1. **Ayarlar > Güvenlik PIN'i** — perde görünmüyorsa PIN'i buradan gir.
2. **Ctrl+Alt+Del → Görev Yöneticisi → MacGuard → Görevi sonlandır.**
3. **Güç düğmesini 10 saniye basılı tut.** Yeniden açtığında MacGuard koruma modunda
   başlamaz; koruma durumu hiçbir yerde saklanmaz.

PIN dosyasını silmek korumayı sıfırlar:

```powershell
Remove-Item "$env:APPDATA\MacGuard\pin.json"
```

---

## Dosyalar

| Ne | Nerede |
|---|---|
| PIN özeti | `%APPDATA%\MacGuard\pin.json` |
| Ayarlar | `%APPDATA%\MacGuard\settings.json` |
| Olay kaydı (son 200) | `%APPDATA%\MacGuard\events.json` |
| Fotoğraflar (son 100) | `%APPDATA%\MacGuard\Snapshots\` |
| Uygulama (install.ps1 ile) | `%LOCALAPPDATA%\Programs\MacGuard\MacGuard.exe` |
| Oturum açılışında başlatma | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\MacGuard` |

---

## Geliştirme

```powershell
dotnet build Windows\MacGuard.Windows.csproj
dotnet publish Windows\MacGuard.Windows.csproj -c Release -r win-x64   # tek dosyalık MacGuard.exe
```

Proje `EnableWindowsTargeting` açık olduğu için Linux ve macOS'ta da derlenir (çalışmaz,
ama derleme hatalarını görmek için yeter). Simge `python Windows\Scripts\make_icon.py` ile
üretilir (Pillow gerekir).

```
Windows/
├── MacGuard.Windows.csproj   .NET 10, WPF, tek bağımlılık NAudio
├── app.manifest              Ekran başına DPI, yönetici yetkisi istemez
├── App.xaml(.cs)             Giriş, tek örnek, bildirim alanı
├── Core/                     Durum makinesi, ayarlar, olay kaydı
├── Sensors/                  Kamera, güç, kapak, USB, girdi, ekran
├── Alarm/                    Siren, ses kontrolü, konuşma, uyku engeli
├── Security/                 PIN saklama, davetsiz misafir fotoğrafları
├── Notify/                   ntfy istemcisi
├── Platform/                 Win32 çağrıları, gizli mesaj penceresi, klavye kilidi
├── UI/                       Panel, ayarlar, tuş takımı, perdeler
├── Scripts/make_icon.py      Simgeyi çizer
└── install.ps1               Kaynaktan derleyip kurar
```

macOS ve Windows sürümleri ayrı kod tabanları; davranış ve metinler bilerek aynı tutuldu.
Bir tarafta mantık değişirse (eşik, sakinleşme, PIN algoritması) diğer tarafa da taşınmalı.
