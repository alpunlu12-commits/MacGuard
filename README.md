# MacGuard

[![Lisans: MIT](https://img.shields.io/badge/lisans-MIT-green.svg)](LICENSE)
![Platform](https://img.shields.io/badge/platform-macOS%2014%2B-lightgrey.svg)
![Swift](https://img.shields.io/badge/swift-5.9-orange.svg)

Kafede masada bıraktığın MacBook için hırsızlık alarmı. Koruma modunu başlatırsın,
bilgisayarına dokunan olursa siren çalar ve ancak senin PIN'inle susar.

macOS 14+ · Apple Silicon ve Intel · Swift + SwiftUI · **bağımlılık yok**

> **English:** MacGuard is a theft-deterrent alarm for MacBooks left unattended
> in cafés. Arm it and a siren goes off if the machine is moved, the lid is
> closed, the charger is unplugged, a USB device is connected, or someone leans
> too close to the camera. Only your PIN silences it. It also grabs a photo from
> the webcam and can push a notification to your phone. Native Swift + SwiftUI,
> zero dependencies, MIT licensed. Interface and docs are in Turkish.

---

## Ekran görüntüleri

<!-- docs/images/ içine dosyaları ekledikten sonra aşağıdaki üç satırın
     yorum işaretlerini kaldır. Ayrıntı: docs/images/README.md

![Ana pencere](docs/images/dashboard.png)
![Koruma ekranı](docs/images/lockscreen.png)
![Ayarlar](docs/images/settings.png)
-->

---

## Kurulum

Xcode gerekmez, **Command Line Tools yeterli**:

```bash
xcode-select --install
```

Sonra:

```bash
git clone <bu-deponun-adresi>
cd MacGuard
./Scripts/build_app.sh --install
```

`build/MacGuard.app` üretilir ve `/Applications` altına kopyalanır.
`--install` vermezsen yalnızca `build/` içinde kalır.

İlk çalıştırma:

```bash
open /Applications/MacGuard.app
```

### macOS "açılamıyor" derse

Uygulama ad-hoc imzalı, yani Apple'a para verip notarize edilmiş değil.
**Kendi derlediğin** ikilide bu sorun çıkmaz. Başka birinden aldıysan
Gatekeeper engeller — o durumda kaynaktan kendin derle. Tavsiyem bu:
bir güvenlik aracını başkasının derlediği hâliyle çalıştırma.

---

## İlk açılışta yapılacaklar

1. **PIN belirle.** Uygulama ilk açılışta sorar. Alarmı durdurabilen tek şey budur.
   PIN düz metin olarak hiçbir yere yazılmaz — 50.000 tur tekrarlanmış, tuzlanmış
   SHA-256 özeti olarak `~/Library/Application Support/MacGuard/pin.json` içinde
   durur (izinler 0600, yalnızca senin kullanıcın okuyabilir).

   > Anahtar Zinciri bilerek kullanılmıyor: MacGuard ad-hoc imzalı ve sık
   > derleniyor; her derlemede imza değiştiği için macOS her açılışta anahtar
   > zinciri onayı isteyip uygulamayı kilitliyordu.
2. **Kamera iznini ver.** "İzin iste" düğmesine bas, macOS'un sorusuna izin ver.
   Hareket ve yakınlık algılama kamerayı kullanır.
3. **Kamerayı ayarla.** Korumayı başlatmadan `Kamerayı Ayarla` ile canlı ölçümü aç.
   İki çubuk birden eşiği geçmeli:
   - bilgisayarı **yerinden oynat** → her iki çubuk da sarı çizgiyi aşmalı
   - **elini kameranın önünde salla** → "kareye yayılma" çubuğu eşiğin altında kalmalı

   İkincisi eşiği aşıyorsa Ayarlar > Hassasiyet > **Kare kaplama eşiği**'ni yükselt.
   Gerçek taşımada çubuk eşiği aşmıyorsa düşür.

---

## Tetikleyiciler

| Tetik | Nasıl çalışır |
|---|---|
| **Hareket** | Kamera kareleri karşılaştırılır; değişim *tüm kareye yayılmışsa* bilgisayar oynatılmış demektir |
| **Yakınlık** | Vision ile yüz kutusu ölçülür; kadrajda fazla büyürse biri çok yaklaşmış demektir |
| **Şarj kablosu** | IOKit güç kaynağı bildirimi — adaptör çıkarıldığı anda |
| **Ekran kapağı** | `IOPMrootDomain / AppleClamshellState` yoklanır |
| **USB aygıt** | IOKit eşleşme bildirimleri — bellek, kablo, aygıt takılır/çıkarılır |
| **Klavye / trackpad** | Sistemin "son girdiden beri geçen süre" sayacı okunur (ek izin istemez) |
| **Harici ekran** | Dock / monitör bağlantısı koparsa |

### Neden hareket kamerayla algılanıyor?

Apple Silicon MacBook'larda ivmeölçer **yok**. O sensör (Sudden Motion Sensor),
sabit diskli eski Intel MacBook'lardaki diski park etmek içindi ve SSD'ye geçişle
birlikte kaldırıldı. Bu yüzden "bilgisayar kaldırıldı" bilgisini donanımdan okumak
mümkün değil; MacGuard bunu görüntüden çıkarıyor. Bilgisayarı kaldırdığında tüm
sahne birden kaydığı için fark çok belirgin oluyor.

Odanın ışığı topluca değişirse tetiklenmemesi için karelerin **ortalama farkı
çıkarılıp** yalnızca yapısal değişim ölçülüyor. Ayrıca tek karelik gürültü alarm
çaldırmasın diye eşiğin iki ardışık karede aşılması şart.

### Yanlış alarmı önleyen iki mekanizma

**1. Kare kaplama (coverage).** Değişimin şiddetine bakmak tek başına yetmiyor:
sen bilgisayarın önünde kıpırdadığında da şiddet yükseliyor. Asıl ayrım şu —
bilgisayar yerinden oynadığında karenin **tamamı** birden değişir; önünde biri
kıpırdadığında yalnızca **bir bölgesi**. MacGuard ızgaradaki kaç hücrenin
değiştiğini sayıyor ve tetik için hem şiddetin hem kaplama oranının eşiği
geçmesini şart koşuyor. Varsayılan kaplama eşiği %50.

**2. Sahne sakinleşmesi (settle).** Korumayı başlattığın anda sen hâlâ
bilgisayarın başındasın — yüzün kadrajda, ellerin klavyede. Bu yüzden hiçbir
sensör devreye girer girmez tetik üretmez:

| Sensör | Nöbete geçme şartı |
|---|---|
| Hareket | ~1,2 saniye boyunca sahne sakin kalmalı |
| Yakınlık | Kadrajda eşiğe yakın yüz kalmamalı (~2 saniye) |
| Klavye / trackpad | 2 saniye hiç dokunulmamalı |

Panelde her sensörün kartında **"Nöbette"** mi yoksa **"Sakinleşme bekleniyor"**
mu yazdığını görürsün — sensör neden çalmıyor sorusu görünmez bir bilmece olmasın.

> Klavye sensörünü masanın başında test ederken buna dikkat et: elini çekip
> 2 saniye beklemeden nöbete geçmez. "Devreye girme gecikmesi" 0 ise bu daha da
> belirgin olur. Sen masanın başında oturmaya devam edersen yakınlık sensörü hiç
nöbete geçmez ve seni asla uyarmaz — ancak sen kalkıp kadraj boşaldıktan sonra
biri yaklaşırsa alarm çalar.

---

## Koruma ekranı

Koruma devreye girdiğinde tüm ekranları kaplayan bir uyarı gösterilir. Kırmızı
panik ekranı değil — sakin, okunur bir tabela:

```
        MACGUARD
        KİLİT ALTINDA

        Bu bilgisayar MacGuard ile korunuyor.

        • Yerinden oynatmak, kapağı kapatmak ya da şarjı çıkarmak alarmı tetikler
        • Alarm yalnızca sahibinin PIN'i ile susturulabilir
        • Tetiklendiği anda kamera fotoğraf çeker ve sahibine bildirim gider

        Lütfen dokunmayın.

        AKTİF SENSÖRLER  [Hareket] [Yakınlık] [Şarj Kablosu] ...
```

Amaç alarm çalmadan **önce** iş görmek: masaya yaklaşan kişi bilgisayara
dokunmadan durumu okusun. Hangi sensörlerin nöbette olduğu rozet rozet listelenir —
caydırıcılık bilginin somut olmasından gelir. Yanında PIN tuş takımı vardır.

- Metni Ayarlar > Koruma ekranı'ndan değiştirebilirsin
- **Önizle (10 sn)** düğmesi korumayı başlatmadan nasıl göründüğünü gösterir
- Özellik tamamen kapatılabilir; o zaman koruma sessizce arka planda çalışır
- PIN tuş takımı **her ekranda** vardır; ikinci ekran takılı olsa da baktığın
  ekranda PIN girebilirsin

> Varsayılan metin bilerek yalnızca uygulamanın gerçekten yaptığı şeyleri sayar.
> Örneğin **konum bilgisi gönderilmiyor** — metne öyle bir satır eklersen ekranda
> yazan şeyin arkasını uygulama dolduramaz.

> Yazının görünmesi için ekran uyutulmaz, bu yüzden koruma açıkken pil tüketimi
> biraz artar.

## Uyarı aşaması (isteğe bağlı)

Ayarlar > Zamanlama > **Uyarı süresi** sıfırdan büyükse, tetik geldiğinde önce
tam siren değil kesik bir **uyarı bipi** çalar, ekranda geri sayım ve PIN tuş
takımı görünür. Süre içinde PIN girersen alarm hiç çalmaz. Girmezsen tam sirene
yükselir — ev ve araç alarmlarındaki mantık.

Varsayılan 0'dır (tetik gelir gelmez tam alarm). Yanlış alarmdan çekiniyorsan
5–10 saniye iyi bir değer.

## Alarm çaldığında

- Sistem sesi zorla ayarlanan seviyeye çıkar, sessize alınmışsa açılır
- **Saniyede bir kontrol edilir:** biri ses tuşuyla kısarsa ya da sessize alırsa
  anında geri getirilir (sesi yükseltene karışılmaz)
- Kulaklık takılıysa çıkış **dahili hoparlöre** alınır
- Siren çalar (kod içinde üretilir, ses dosyası taşınmaz)
- Türkçe sesli uyarı okunur
- Tüm ekranları kırmızı bir perde kaplar, **her ekranda** PIN tuş takımı
- Dock, menü çubuğu, ⌘-Tab, ⌘-Q ve Zorla Çık devre dışı kalır
- Kameradan davetsiz misafirin fotoğrafı çekilir
- Açıksa telefonuna bildirim + fotoğraf gider

Koruma açıkken uygulama **kapatılamaz**; ⌘Q sessizce reddedilir ve perde öne
alınır. (Eskiden bir uyarı kutusu açılıyordu; kutu perdenin arkasında kaldığı
için uygulama görünmeyen bir modal'a kilitleniyor ve PIN girilemiyordu.)

### Perde nöbetçisi

Perde bir kez kurulup unutulmaz. `OverlayGuardian` iki şeye bakar:

- **Ekran düzeni değişti mi** — kapak kapanıp açıldığında, ikinci ekran takılıp
  çıkarıldığında perde o anki ekran listesine göre yeniden kurulur. Yeni ekrana
  perde konur, kaybolan ekranınki atılır, kalanların çerçevesi düzeltilir.
- **Kullanıcı az önce dokundu mu** — `InputSensor`'ın izin gerektirmeden okuduğu
  "son girdiden bu yana geçen süre" sayacı 1.2 sn'nin altındaysa perde öne
  alınır ve klavye **farenin bulunduğu ekrandaki** tuş takımına verilir.

Yani alarm çalarken ⌘-Tab'a bassan, başka pencereye geçmeye çalışsan ya da
perde ikinci ekranda kalmış olsa bile, ekrana dokunduğun anda perde geri gelir.

### Güvenlik PIN'i (ikinci kapı)

Yine de PIN ekranına ulaşamadığın bir durum olursa: **Ayarlar > Güvenlik PIN'i**
(en altta) alarm çalarken PIN'ini girip durdurmanı sağlar. Yanlış PIN ve bekleme
cezası perdedekiyle aynı sayaçtan işler — ikinci bir kapı, gevşek bir kapı değil.

---

## Test / demo ses seviyesi

Ayarlar > Alarm > **Alarm ses seviyesi** kaydırağı, alarmın sistem sesini hangi
seviyeye getireceğini belirler. Evde test ederken düşür — ev halkı rahatsız
olmasın.

%100'ün altındayken ana panelde kalıcı bir sarı uyarı çubuğu çıkar
(*"Alarm sesi test seviyesinde"*) ve tek tıkla %100'e dönebilirsin. Çekim
öncesi unutmamak için.

Ses kontrolü AppleScript yerine **CoreAudio** ile yapılır: saniyede bir
yoklanacağı için hızlı ve iş parçacığı güvenli olması gerekiyordu.

## Davetsiz misafir fotoğrafları

Alarm çaldığı anda kameradan bir kare alınır ve **diske yazılır**:

```
~/Library/Application Support/MacGuard/Snapshots/
└── 2026-09-22_22-21-20_motion.jpg
```

Dosya adı tarih-saat ve tetiğin türünü taşır. Klasör `0700`, dosyalar `0600` —
yalnızca senin kullanıcın okuyabilir. En son **100 kare** saklanır, daha eskiler
otomatik silinir.

Panelde olay kaydının üstündeki **"N fotoğraf"** düğmesi klasörü Finder'da açar
ve en yeni kareyi seçili gösterir. Ayarlar > Alarm'dan da açılabilir.

Telefon bildirimi kapalı olsa bile fotoğraf kaydedilir; bildirim açıksa ayrıca
telefona ek olarak gönderilir.

> Masaüstü / Belgeler / İndirilenler gibi korunan klasörlere bilerek yazılmıyor.
> Oralara yazmak macOS'tan ayrı bir izin ister ve MacGuard ad-hoc imzalı olduğu
> için o izin her yeni derlemede yeniden sorulur.

## Telefona bildirim (ntfy)

Hesap, API anahtarı, kurulum yok.

1. Telefonuna **ntfy** uygulamasını kur (iOS / Android, ücretsiz)
2. MacGuard > Ayarlar > Telefona bildirim → aç
3. `Rastgele konu üret` ile bir konu adı al
4. Telefondaki ntfy'de aynı konu adına abone ol
5. `Test bildirimi gönder` ile dene

> Konu adı bilen herkesin okuyabileceği bir adrestir. Tahmin edilemeyecek bir
> isim kullan — üretilen rastgele ad bunun için.

---

## Kapak kapanınca alarm susmasın

Normalde kapak kapanınca macOS uyur ve her şey durur. Ayarlar > Alarm >
**"Kapak kapansa bile uyuma"** seçeneği koruma süresince `pmset disablesleep`
ayarını açar, koruma kapanınca otomatik geri alır.

`pmset` root yetkisi ister ve macOS'ta kapak uykusunu root olmadan engellemenin
başka bir yolu yoktur. İki seçeneğin var:

**a) Her seferinde şifre gir.** Varsayılan davranış. Koruma her açıldığında
macOS'un yönetici penceresi çıkar.

**b) Bir kez kur, bir daha sorulmasın.** Aynı ayar bölümündeki
**"Şifreyi bir kez gir, bir daha sorma"** düğmesi `/etc/sudoers.d/macguard`
dosyasına dar kapsamlı bir kural yazar:

```
<kullanıcı-adın> ALL=(root) NOPASSWD: /usr/bin/pmset -a disablesleep 1, /usr/bin/pmset -a disablesleep 0
```

Kapsam bilerek iki komutla sınırlıdır — başka hiçbir şey root olarak
çalıştırılamaz. Dosya yazılmadan önce `visudo -cf` ile doğrulanır, böylece bozuk
bir kural `sudo`yu kıramaz. Aynı yerden istediğin an kaldırabilirsin.

Bu bir yetki devridir; ne yaptığını bilerek aç. Rahat değilsen (a) şıkkında kal.

### Uyanınca alarm devam eder

Seçenek kapalıyken de kapak kapanması yakalanır ve alarm başlar; sistem uyursa
ses kesilir ama **Mac uyanır uyanmaz siren kaldığı yerden devam eder.** Hırsız
kapağı açtığı anda alarm yeniden patlar. Bu davranış için hiçbir izin gerekmez.

## Uygulama çökerse

Uyku engeli açıkken uygulama çökerse Mac bir daha uyumayabilir. MacGuard bunu
bir bayrakla işaretler ve bir sonraki açılışta ayarı kendiliğinden geri alır
(şifresiz kural kuruluysa sessizce).

---

## Sınırlar — dürüst liste

- **Zorla kapatma durdurur.** Güç düğmesine 10 saniye basılırsa Mac kapanır ve
  alarm susar. Yazılımla engellenebilecek bir şey değil.
- **Kamera ışığı yanar.** macOS kamera kullanımında turuncu göstergeyi donanım
  seviyesinde yakar; kapatılamaz ve kapatılmamalı.
- **Kapak kapalıyken kamera kör olur.** Kapak tetiği ve diğer sensörler çalışmaya
  devam eder, hareket algılama çalışmaz.
- **Bu bir caydırıcıdır, kilit değildir.** Gürültü çıkarır ve delil toplar;
  bilgisayarın çalınmasını fiziksel olarak engellemez.
- **Bundle kimliği benzersiz olmalı.** Aynı `CFBundleIdentifier` değerini taşıyan
  başka bir derleme diskte duruyorsa macOS'un izin sistemi ve Launch Services
  hangisinin hangisi olduğunu karıştırabilir. Eski Xcode derlemeleri varsa
  temizle: `rm -rf ~/Library/Developer/Xcode/DerivedData/MacGuard-*`

---

## ⚠️ Acil durum: PIN'i unuttum / uygulama takıldı

Alarm çalarken MacGuard bilerek kaçış yollarını kapatır: Dock, menü çubuğu,
⌘-Tab, ⌘-Q ve **Zorla Çık** devre dışıdır. Hırsızlık alarmı olmasının anlamı bu.

Ama bu senin de kilitli kalabileceğin anlamına gelir. Kurtarma yolu:

**Güç düğmesini 10 saniye basılı tut.** Mac kapanır. Yeniden açtığında MacGuard
koruma modunda başlamaz — koruma durumu hiçbir yerde saklanmaz. PIN'i
uygulamadan yeniden belirleyebilirsin.

PIN dosyasını doğrudan silmek de korumayı sıfırlar:

```
rm ~/Library/Application\ Support/MacGuard/pin.json
```

> Bu, uygulamanın bir kilit değil **caydırıcı** olduğunu da gösteriyor:
> bilgisayarına fiziksel erişimi olan biri her zaman gücü kesebilir.
> MacGuard gürültü çıkarır, fotoğraf çeker ve haber verir — çalınmayı
> fiziksel olarak engellemez.

## Güvenlik modeli — dürüst özet

**Neyi korur:** kafede/kütüphanede kısa süre başından ayrıldığın bilgisayarı.
Ses çıkararak caydırır, kanıt toplar, sana haber verir.

**Neyi korumaz:** kararlı ve hazırlıklı bir hırsızı. Güç kesilirse her şey durur.

**PIN:** `~/Library/Application Support/MacGuard/pin.json` içinde tuzlanmış,
50.000 turlu SHA-256 özeti olarak (izin 0600). Düz metin saklanmaz. Ama 4 haneli
bir PIN'in özeti, dosyayı okuyabilen biri için kaba kuvvetle kırılabilir —
dosyayı okuyabilmesi zaten senin oturumuna erişmesi demektir. Uzun PIN kullan.

**Kamera:** görüntü bilgisayarından çıkmaz. Tek istisna: telefon bildirimini
**sen** açarsan alarm anındaki tek kare ntfy'ye gönderilir.

**ntfy konu adı bir paroladır.** Konuyu bilen herkes o konuya düşen fotoğrafları
görebilir. Uygulamanın ürettiği rastgele ad 20 karakterdir (~103 bit); kendi
adını yazacaksan tahmin edilebilir bir şey seçme. Konu adında yalnızca İngiliz
alfabesi harfleri, rakam, `_` ve `-` kullanılabilir — uygulama diğerlerini
reddeder.

**sudoers kuralı** isteğe bağlıdır ve yalnızca iki `pmset` komutunu kapsar.
Kurmazsan MacGuard hiçbir root yetkisi kullanmaz.

**Kum havuzu yok, notarizasyon yok.** Uygulama ad-hoc imzalıdır; kendi
bilgisayarında derleyip çalıştırman için tasarlandı. Başkasının derlediği bir
ikiliyi çalıştırmadan önce kaynağa bak.

## Proje yapısı

```
MacGuard/
├── Package.swift            SPM tanımı (bağımlılık yok)
├── Resources/Info.plist     Bundle kimliği, izin metinleri
├── Scripts/
│   ├── build_app.sh         .app paketi üretir ve imzalar
│   └── make_icon.swift      Uygulama ikonunu çizer
└── Sources/MacGuard/
    ├── App/                 Giriş, menü çubuğu, künye, giriş öğesi
    ├── Core/                Durum makinesi, ayarlar, olay kaydı
    ├── Sensors/             Kamera, güç, kapak, USB, girdi, ekran
    ├── Alarm/               Siren, ses kontrolü, konuşma, uyku engeli
    ├── Security/            PIN saklama, davetsiz misafir fotoğrafları
    ├── Notify/              ntfy istemcisi
    └── UI/                  Panel, ayarlar, tuş takımı, perdeler
```

## Geliştirme

```bash
swift build                 # hızlı derleme
swift build -c release      # yayın derlemesi (sıfır uyarı vermeli)
./Scripts/build_app.sh      # .app paketi üret
```

Xcode gerekmez; Command Line Tools yeterli.

Katkı vermek istersen [CONTRIBUTING.md](CONTRIBUTING.md), güvenlikle ilgili bir
bulgu için [SECURITY.md](SECURITY.md).

### Mimari notu

Uygulamanın en ilginç kısmı hareket algılama. Apple Silicon Mac'lerde ivmeölçer
olmadığı için hareket kameradan çıkarılıyor ve "bilgisayar oynadı" ile "önünde
biri kıpırdadı" ayrımı **kare kaplama oranıyla** yapılıyor — detay yukarıdaki
[Tetikleyiciler](#tetikleyiciler) bölümünde.

---

## Lisans ve künye

MIT Lisansı — özgürce kullan, değiştir, dağıt. Tek şart telif satırını koruman.
Ayrıntı için [LICENSE](LICENSE).

**Yapan:** Alp Ünlü — [Instagram @alppunlu](https://instagram.com/alppunlu) · [YouTube @alpunlu](https://youtube.com/@alpunlu)

Bu proje bir video içeriği için sıfırdan yazıldı. Sorun bulursan ya da
geliştirirsen memnun olurum.
