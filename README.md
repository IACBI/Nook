<a id="top"></a>
# Nook

Per-process VRAM, GPU load, temperature and clock on Windows — one portable executable, with an overlay you can pin over a game.

[![build](https://github.com/IACBI/Nook/actions/workflows/build.yml/badge.svg)](https://github.com/IACBI/Nook/actions/workflows/build.yml)
[![latest release](https://img.shields.io/github/v/release/IACBI/Nook)](https://github.com/IACBI/Nook/releases/latest)
![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![MIT](https://img.shields.io/badge/license-MIT-green)

**Read this in:** [English](#english) · [Türkçe](#turkce)

---

<a id="english"></a>
## English

### Overview

Nook answers one question well: how much GPU memory is this process actually using right now? It reads the same WDDM performance counters Task Manager uses, adds temperature and core clock from the vendor driver, and shows the result either in a small window or in a transparent overlay that sits above fullscreen games.

There is no installer, no service and no code injected into other processes. You run a single executable and close it when you are done.

### Features

- GPU memory for any running process: dedicated (current, peak and average) and shared (current and peak)
- Adapter load, memory, temperature and core clock, refreshed once per second
- Multiple GPUs, enumerated through DXGI and matched by LUID
- Overlay with a click-through lock and corner presets; it remembers its position and comes back onto a visible screen when a monitor is unplugged
- Follows the monitored program when it restarts under a new PID
- Temperature and clock readings recover on their own after a driver reset or update
- Runs from the tray, optionally starting with Windows; launching it again brings the running window forward
- No network traffic of any kind

### Requirements

- Windows 10 or 11, 64-bit
- A WDDM 2.0 or newer display driver
- For temperature and clock: NVIDIA's `nvml.dll` or AMD's `atiadlxx.dll`, both installed with the regular driver. Without them those two lines are left out of the overlay.
- .NET 10 SDK, only to build from source

### Installation

Download `Nook.exe` from the [latest release](https://github.com/IACBI/Nook/releases/latest). Releases from 1.0.6 on carry a build provenance attestation, so you can check that the file was built by this repository's release workflow:

```powershell
gh attestation verify Nook.exe -R IACBI/Nook
```

To build it yourself:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

The result is a single `release\Nook.exe` that needs nothing else installed.

### Usage

The **Process** tab lists everything running. Search by name, or tick *Only VRAM active* to hide processes that never touch the GPU. Pick one and the counters start filling in.

The **GPU** tab shows the selected adapter: engine load, dedicated and shared memory, temperature and core clock.

Two readings need a word of explanation:

- **0 MB dedicated is usually correct.** Integrated GPUs have no dedicated memory, and a laptop's discrete GPU keeps almost everything in the shared pool while it is parked. The overlay follows whichever pool is actually in use.
- **`No driver sensor`** means the vendor driver offers no temperature or clock API. That is always the case for Intel adapters.

The overlay is controlled from the bottom bar, or from anywhere with two shortcuts:

| Shortcut | Action |
|---|---|
| `Ctrl+Shift+V` | Show or hide the overlay |
| `Ctrl+Shift+L` | Switch between click-through and draggable |

While the overlay is draggable it shows a `MOVE` badge and a dashed border. Drag it where you want it, then lock it again so clicks pass straight through to the game underneath.

### Configuration

Settings live in `%LocalAppData%\Nook\settings.json` and are saved a moment after each change. The file holds the selected GPU and process, the overlay position and lock state, the tray preferences, and the two shortcuts as Windows virtual-key and modifier values.

Deleting the file restores every default. A file that cannot be read is kept as `settings.bad.json` instead of being overwritten, and a single out-of-range value falls back to its own default.

When something goes wrong, `%LocalAppData%\Nook\nook.log` records it. Please attach that file to a bug report.

### Contributing

Issues and pull requests are welcome. Keep to the style already in the files, and before opening a pull request make sure both of these pass:

```powershell
dotnet build Nook.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests/Nook.Tests
```

Read [docs/architecture.md](docs/architecture.md) before touching the native code, and [docs/releasing.md](docs/releasing.md) before changing the release workflow.

### License

[MIT](LICENSE) © 𝓐.𝓒.𝓑

[⬆ Back to top](#top)

---

<a id="turkce"></a>
## Türkçe

### Genel Bakış

Nook tek bir soruyu iyi cevaplar: bu süreç şu anda gerçekte ne kadar GPU belleği kullanıyor? Görev Yöneticisi'nin kullandığı WDDM performans sayaçlarını okur, ekran kartı sürücüsünden sıcaklık ve çekirdek saat hızını alır ve sonucu ister küçük bir pencerede, ister tam ekran oyunların üstünde duran şeffaf bir overlay'de gösterir.

Kurulum sihirbazı, arka planda çalışan bir servis ya da başka süreçlere enjekte edilen kod yoktur. Tek bir dosyayı çalıştırırsınız, işiniz bitince kapatırsınız.

### Özellikler

- Çalışan her süreç için GPU belleği: adanmış (anlık, en yüksek ve ortalama) ve paylaşımlı (anlık ve en yüksek)
- Saniyede bir yenilenen adaptör yükü, bellek, sıcaklık ve çekirdek saat hızı
- DXGI ile bulunan ve LUID ile eşleştirilen birden fazla GPU desteği
- Tıklamaları geçiren kilit modu ve köşe konumları olan overlay; yerini hatırlar, monitör çıkarıldığında görünen bir ekrana geri gelir
- İzlenen program yeni bir PID ile yeniden başladığında onu takip etmeye devam eder
- Sürücü sıfırlandığında ya da güncellendiğinde sıcaklık ve saat hızı değerleri kendiliğinden geri gelir
- Sistem tepsisinde çalışır, istenirse Windows ile birlikte açılır; yeniden başlatıldığında açık olan pencereyi öne getirir
- Hiçbir şekilde ağ trafiği oluşturmaz

### Gereksinimler

- Windows 10 veya 11, 64-bit
- WDDM 2.0 veya daha yeni bir ekran kartı sürücüsü
- Sıcaklık ve saat hızı için NVIDIA'nın `nvml.dll` ya da AMD'nin `atiadlxx.dll` dosyası; ikisi de normal sürücüyle birlikte kurulur. Bulunmadıklarında bu iki satır overlay'de gösterilmez.
- .NET 10 SDK, yalnızca kaynaktan derlemek için

### Kurulum

`Nook.exe` dosyasını [son sürümden](https://github.com/IACBI/Nook/releases/latest) indirin. 1.0.6 ve sonraki sürümlerin bir build provenance attestation'ı vardır; dosyanın bu deponun release iş akışında derlendiğini şöyle doğrulayabilirsiniz:

```powershell
gh attestation verify Nook.exe -R IACBI/Nook
```

Kendiniz derlemek için:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

Sonuçta başka hiçbir kurulum gerektirmeyen tek bir `release\Nook.exe` dosyası elde edersiniz.

### Kullanım

**Process** sekmesi çalışan tüm süreçleri listeler. Ada göre arayabilir ya da *Only VRAM active* kutusunu işaretleyerek GPU'yu hiç kullanmayan süreçleri gizleyebilirsiniz. Birini seçtiğinizde değerler gelmeye başlar.

**GPU** sekmesi seçili adaptörü gösterir: motor yükü, adanmış ve paylaşımlı bellek, sıcaklık ve çekirdek saat hızı.

İki değer biraz açıklama ister:

- **0 MB adanmış bellek genellikle doğrudur.** Tümleşik ekran kartlarının adanmış belleği yoktur; dizüstü bilgisayardaki harici kart da boştayken neredeyse her şeyi paylaşımlı havuzda tutar. Overlay hangi havuz gerçekten kullanılıyorsa onu gösterir.
- **`No driver sensor`**, sürücünün sıcaklık ya da saat hızı için bir arayüz sunmadığı anlamına gelir. Intel adaptörlerinde durum her zaman budur.

Overlay alttaki çubuktan ya da her yerden çalışan iki kısayolla yönetilir:

| Kısayol | İşlev |
|---|---|
| `Ctrl+Shift+V` | Overlay'i göster veya gizle |
| `Ctrl+Shift+L` | Tıklamaları geçiren mod ile sürüklenebilir mod arasında geçiş yap |

Sürüklenebilir moddayken overlay'de bir `MOVE` etiketi ve kesikli bir çerçeve görünür. Overlay'i istediğiniz yere taşıyın, sonra yeniden kilitleyin; tıklamalar doğrudan alttaki oyuna geçer.

### Yapılandırma

Ayarlar `%LocalAppData%\Nook\settings.json` dosyasında tutulur ve her değişiklikten kısa bir süre sonra kaydedilir. Dosyada seçili GPU ve süreç, overlay'in konumu ve kilit durumu, sistem tepsisi tercihleri ve iki kısayol, Windows sanal tuş ve değiştirici değerleri olarak yer alır.

Dosyayı silmek tüm varsayılanları geri getirir. Okunamayan bir dosyanın üzerine yazılmaz, `settings.bad.json` adıyla saklanır; aralık dışındaki tek bir değer ise yalnızca kendi varsayılanına döner.

Bir sorun çıktığında ayrıntılar `%LocalAppData%\Nook\nook.log` dosyasına yazılır. Hata bildirirken lütfen bu dosyayı ekleyin.

### Katkı

Hata bildirimleri ve pull request'ler memnuniyetle karşılanır. Dosyalardaki mevcut yazım tarzına uyun ve pull request açmadan önce şu iki komutun sorunsuz geçtiğinden emin olun:

```powershell
dotnet build Nook.csproj -c Release -p:TreatWarningsAsErrors=true
dotnet test tests/Nook.Tests
```

Native koda dokunmadan önce [docs/architecture.md](docs/architecture.md), release iş akışını değiştirmeden önce [docs/releasing.md](docs/releasing.md) dosyasını okuyun.

### Lisans

[MIT](LICENSE) © 𝓐.𝓒.𝓑

[⬆ Başa Dön](#top)
