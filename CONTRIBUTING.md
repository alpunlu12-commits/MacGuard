# Katkı

Bu proje bir video içeriği için sıfırdan yazıldı; küçük ve okunur kalmasını
istiyorum. Katkıya açığım.

## Başlamadan

```bash
git clone <depo-adresi>
cd MacGuard
swift build                 # hızlı derleme
./Scripts/build_app.sh      # .app paketi üret
```

Xcode gerekmez — Command Line Tools yeterli.

## Kod tarzı

- **Yorumlar Türkçe.** Neyi değil **niçin** öyle yaptığını yaz.
- Bağımlılık eklemeyelim. Şu an sıfır bağımlılık var, böyle kalsın.
- **Ana iş parçacığını bloklama.** Bu projede `Process`, Anahtar Zinciri ve
  benzeri çağrılar uygulamayı kilitledi; ikisi de ayıklaması zor hatalardı.
  Alt süreç ve dosya sistemi işleri arka plan kuyruğuna.
- Sensör durumu hangi kuyrukta okunuyorsa orada yazılsın. Veri yarışı istemiyoruz.

## Pull request açarken

- `swift build -c release` **sıfır uyarı** vermeli.
- Davranış değiştiren bir şey yaptıysan README'yi de güncelle.
- Güvenlikle ilgili bir bulgu varsa PR yerine [SECURITY.md](SECURITY.md).
