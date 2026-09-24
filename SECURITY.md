# Güvenlik

## Açık bildirimi

MacGuard bir güvenlik aracı. Bir açık bulursan **herkese açık issue açma** —
önce bana özel olarak ulaş:

- Instagram DM: [@alppunlu](https://instagram.com/alppunlu)

Düzeltene kadar detayı paylaşmamanı rica ediyorum.

## Tehdit modeli

MacGuard'ın ne yapıp ne yapmadığı konusunda net olalım.

**Koruduğu senaryo:** kafede, kütüphanede, ofiste kısa süreliğine başından
ayrıldığın bilgisayar. Ses çıkararak caydırır, kanıt toplar, sana haber verir.

**Korumadığı senaryo:** kararlı ve hazırlıklı bir hırsız. Güç düğmesine 10 saniye
basılırsa Mac kapanır ve alarm susar. Bu yazılımla engellenebilecek bir şey değil.

MacGuard bir **caydırıcıdır**, bir kilit değil.

## Veri nerede duruyor

| Ne | Nerede | İzin |
|---|---|---|
| PIN özeti | `~/Library/Application Support/MacGuard/pin.json` | `0600` |
| Alarm fotoğrafları | `~/Library/Application Support/MacGuard/Snapshots/` | `0700` / `0600` |
| Olay kaydı | `~/Library/Application Support/MacGuard/events.json` | `0600` |
| Ayarlar | `UserDefaults` (`com.alpunlu.macguard`) | — |

Hiçbiri bilgisayardan dışarı çıkmaz. **Tek istisna:** telefon bildirimini sen
açarsan alarm anındaki tek kare ve tetik mesajı ntfy sunucusuna gönderilir.
Kullanıcı adı, makine adı ya da konum bilgisi gönderilmez.

## Bilinen sınırlar

- **PIN kaba kuvvetle kırılabilir.** Tuzlanmış, 50.000 turlu SHA-256 kullanıyoruz
  ama 4 haneli bir PIN'in arama uzayı küçük. Dosyayı okuyabilen biri zaten senin
  oturumuna erişmiş demektir. Uzun PIN kullan.
- **ntfy konu adı bir paroladır.** Konuyu bilen herkes o konuya düşen fotoğrafları
  görebilir. Uygulamanın ürettiği rastgele ad 20 karakterdir (~103 bit).
- **Kum havuzu ve notarizasyon yok.** Uygulama ad-hoc imzalıdır; kendi
  bilgisayarında derleyip çalıştırman için tasarlandı. Başkasının derlediği bir
  ikiliyi çalıştırmadan önce kaynağa bak.
- **İsteğe bağlı sudoers kuralı** yalnızca `pmset -a disablesleep 0/1` komutlarını
  kapsar. Kurmazsan MacGuard hiçbir root yetkisi kullanmaz.
