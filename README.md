# CircleTools — by thezencirs

**Windows'ta ön plandaki pencereyi tek kısayolla (`Win + Ctrl + T`) "Her Zaman Üstte" (Always on Top) yapın.** Sabitlenen pencerenin etrafında tıklamaları geçiren **mavi bir çerçeve** görünür; böylece hangi pencerenin sabit olduğu bir bakışta anlaşılır.

> **English:** A tiny, zero-dependency Windows tray utility crafted by [**@thezencirs**](https://github.com/thezencirs). Press **`Win + Ctrl + T`** to pin the active window on top of all others (marked by a click-through blue border); press again to unpin. Single ~30 KB `.exe`, 100% original code under the MIT License, self-updating via GitHub Releases (SHA-256 verified).

[![Geliştirici](https://img.shields.io/badge/developer-thezencirs-orange)](https://github.com/thezencirs)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)
![Lisans](https://img.shields.io/badge/license-MIT-green)

---

## Özellikler

- **`Win + Ctrl + T`** kısayolu ile ön plandaki pencereyi anında üstte sabitle / bırak
- Sabit pencerenin etrafında **mavi çerçeve** (tıklamaları geçirir, pencereyle birlikte hareket eder, küçültülünce gizlenir, kapanınca otomatik silinir)
- Sistem tepsisi (Tray) menüsü: sabitlenen pencere sayısı, **Tüm sabitlemeleri kaldır**, **Windows ile başlat**
- Per-Monitor Yüksek DPI (%125, %150, %200...) tam uyumu
- Tek `.exe` (~30 KB): ek kurulum veya harici çalışma zamanı gerektirmez
- Uygulama içinden güvenli güncelleme (GitHub Releases + `SHA256SUMS.txt` doğrulaması)

## İndirme ve Kurulum

1. [**Releases**](https://github.com/thezencirs/CircleTools/releases/latest) sayfasından `CircleTools-v1.0.0-win.zip` (veya doğrudan `CircleTools.exe`) dosyasını indirin.
2. İstediğiniz bir klasöre çıkarın ve `CircleTools.exe` dosyasına çift tıklayın.

> **Not:** CircleTools pencere açmayan bir **sistem tepsisi (tray) uygulamasıdır**. Çalıştığında sağ alttaki bildirim alanında simgesi görünür. İlk açılışta kısa bir kullanım rehberi ve *"Windows ile birlikte başlasın mı?"* sorusu ekrana gelir.

## Kullanım

1. Üstte tutmak istediğiniz pencereye tıklayın.
2. **`Win + Ctrl + T`** tuşlarına basın — pencere üstte sabitlenir ve etrafında mavi çerçeve belirir.
3. Aynı tuşlara tekrar basın — sabitleme ve mavi çerçeve kalkar.

## Kaynak Koddan Derleme

Ek hiçbir paket veya IDE kurulumu gerekmez; Windows'un yerleşik .NET Framework 4 C# derleyicisi (`csc.exe`) ile doğrudan derlenir:

```powershell
git clone https://github.com/thezencirs/CircleTools.git
cd CircleTools
powershell -ExecutionPolicy Bypass -File build.ps1
```

## Telif Hakkı ve Lisans (Copyright & Legal Notice)

- **Geliştirici & İmza:** [**thezencirs**](https://github.com/thezencirs)
- **Lisans:** Bu projenin tüm kaynak kodları ve görsel varlıkları özgün olarak geliştirilmiş olup [MIT Lisansı](LICENSE) (`Copyright (c) 2026 thezencirs`) altında açık kaynak olarak sunulmaktadır.
- **Marka Beyanı:** CircleTools bağımsız bir açık kaynak projedir; Microsoft Corporation veya diğer üçüncü taraf şirket/markalarla herhangi bir bağlantısı veya ortaklığı yoktur. Windows, Microsoft Corporation'ın tescilli markasıdır.
