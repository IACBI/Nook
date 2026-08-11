<a id="top"></a>
# Nook

Per-process VRAM, GPU load, temperature and clock on Windows — one portable executable, with an overlay you can pin over a game.

[![build](https://github.com/IACBI/Nook/actions/workflows/build.yml/badge.svg)](https://github.com/IACBI/Nook/actions/workflows/build.yml)
[![latest release](https://img.shields.io/github/v/release/IACBI/Nook)](https://github.com/IACBI/Nook/releases/latest)
![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![MIT](https://img.shields.io/badge/license-MIT-green)

**Read this in:** [English](#english) · [Türkçe](#türkçe) · [中文](#中文) · [हिन्दी](#हिन्दी) · [Español](#español) · [العربية](#العربية) · [Português](#português) · [Русский](#русский)

---

<a id="english"></a>
## English

### Overview

Nook answers one question well: how much GPU memory is this process actually using right now? It reads the same WDDM performance counters Task Manager uses, adds temperature and core clock from the vendor driver, and puts the result either in a small window or in a transparent overlay that sits above fullscreen games.

There is no installer, no service and no code injected into other processes. You run a single executable and close it when you are done.

### Features

- Dedicated and shared GPU memory for any running process — current, peak and average
- Adapter load, memory, temperature and core clock, refreshed once per second
- Multiple GPUs, enumerated through DXGI and matched by LUID
- Overlay with a click-through lock, corner presets and a remembered position
- Automatic re-attach when the monitored program restarts under a new PID
- Runs from the tray, optionally starting with Windows
- No network traffic of any kind

### Requirements

- Windows 10 or 11, 64-bit
- A WDDM 2.0 or newer display driver
- For temperature and clock: NVIDIA's `nvml.dll` or AMD's `atiadlxx.dll`, both installed with the regular driver. Without them those two lines are simply left out of the overlay.
- .NET 10 SDK, to build from source

### Installation

Download `Nook.exe` from the [latest release](https://github.com/IACBI/Nook/releases/latest), or build it yourself:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

The result is a single `release\Nook.exe` that needs nothing else installed.

### Usage

The **Process** tab lists everything running; search by name, or tick *Only VRAM active* to hide the processes that never touch the GPU. Pick one and the counters start filling in.

The **GPU** tab shows the selected adapter: engine load, dedicated and shared memory, temperature and core clock.

Two readings are worth explaining. Integrated GPUs have no dedicated memory at all, and a laptop's discrete GPU keeps almost everything in the shared pool while it is parked, so a dedicated figure of 0 MB is usually accurate rather than broken — the overlay follows whichever pool is actually in use. Temperature and clock come from the vendor driver, and Intel exposes no such API, so its adapters report `No driver sensor`.

The overlay is controlled from the bottom bar or from anywhere with two shortcuts:

| Shortcut | Action |
|---|---|
| `Ctrl+Shift+V` | Show or hide the overlay |
| `Ctrl+Shift+L` | Switch between click-through and draggable |

While the overlay is draggable it shows a `MOVE` badge and a dashed border; drag it anywhere, then lock it again so clicks pass straight through to the game underneath.

### Configuration

Settings live in `%LocalAppData%\Nook\settings.json` and are written when the application exits. The file holds the selected GPU and process, the overlay position and lock state, the tray preferences, and the two hotkeys as Windows virtual-key and modifier values.

Deleting the file restores every default.

### Contributing

Issues and pull requests are welcome. Please keep to the style already in the files, and make sure `dotnet build -c Release` finishes without warnings and `dotnet test tests/Nook.Tests` passes before you open a pull request. If you are touching the native code, read [docs/architecture.md](docs/architecture.md) first.

### License

[MIT](LICENSE) © 𝓐.𝓒.𝓑

[⬆ Back to top](#top)

---

<a id="türkçe"></a>
## Türkçe

### Genel Bakış

Nook tek bir soruya iyi cevap verir: bu süreç şu anda ne kadar GPU belleği kullanıyor? Görev Yöneticisi'nin okuduğu WDDM performans sayaçlarını okur, üzerine sürücüden gelen sıcaklık ve çekirdek saat hızını ekler, sonucu ister küçük bir pencerede ister tam ekran oyunların üzerinde duran şeffaf bir overlay'de gösterir.

Kurulum sihirbazı, arka plan servisi ya da başka süreçlere kod enjeksiyonu yok. Tek bir çalıştırılabilir dosyayı açar, işiniz bitince kapatırsınız.

### Özellikler

- Çalışan herhangi bir süreç için adanmış ve paylaşımlı GPU belleği — anlık, en yüksek ve ortalama
- Saniyede bir yenilenen adaptör yükü, bellek, sıcaklık ve çekirdek saat hızı
- DXGI ile listelenen ve LUID ile eşleştirilen birden çok GPU
- Tıklama geçişli kilidi, köşe hazır konumları ve konumu hatırlanan overlay
- İzlenen program yeni bir PID ile yeniden başladığında otomatik bağlanma
- Sistem tepsisinden çalışma, istenirse Windows ile birlikte açılma
- Hiçbir biçimde ağ trafiği yok

### Gereksinimler

- Windows 10 veya 11, 64-bit
- WDDM 2.0 veya üzeri ekran kartı sürücüsü
- Sıcaklık ve saat hızı için NVIDIA'nın `nvml.dll` ya da AMD'nin `atiadlxx.dll` dosyası; ikisi de normal sürücüyle birlikte kurulur. Bulunmadıklarında o iki satır overlay'de hiç görünmez.
- Kaynaktan derlemek için .NET 10 SDK

### Kurulum

`Nook.exe` dosyasını [son sürümden](https://github.com/IACBI/Nook/releases/latest) indirin ya da kendiniz derleyin:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

Sonuç, başka hiçbir şeye ihtiyaç duymayan tek bir `release\Nook.exe` dosyasıdır.

### Kullanım

**Process** sekmesi çalışan her şeyi listeler; ada göre arayabilir ya da *Only VRAM active* seçeneğiyle GPU'ya hiç dokunmayan süreçleri gizleyebilirsiniz. Birini seçtiğinizde sayaçlar dolmaya başlar.

**GPU** sekmesi seçili adaptörü gösterir: motor yükü, adanmış ve paylaşımlı bellek, sıcaklık ve çekirdek saat hızı.

İki değerin açıklanmaya ihtiyacı var. Tümleşik ekran kartlarının adanmış belleği hiç yoktur; dizüstü bilgisayarlardaki ayrık kart da boştayken neredeyse her şeyi paylaşımlı havuzda tutar. Bu yüzden 0 MB'lık adanmış bellek genelde bir arıza değil, doğru ölçümdür — overlay hangi havuz kullanılıyorsa onu gösterir. Sıcaklık ve saat hızı ise sürücüden gelir; Intel böyle bir arayüz sunmadığı için onun adaptörlerinde `No driver sensor` yazar.

Overlay alttaki çubuktan ya da her yerden çalışan iki kısayolla yönetilir:

| Kısayol | İşlev |
|---|---|
| `Ctrl+Shift+V` | Overlay'i göster veya gizle |
| `Ctrl+Shift+L` | Tıklama geçişli ile sürüklenebilir arasında geçiş yap |

Sürüklenebilir haldeyken overlay `MOVE` rozeti ve kesik çizgili bir çerçeve gösterir; istediğiniz yere taşıyıp tekrar kilitleyin, böylece tıklamalar doğrudan alttaki oyuna geçer.

### Yapılandırma

Ayarlar `%LocalAppData%\Nook\settings.json` dosyasında tutulur ve uygulama kapanırken yazılır. Dosyada seçili GPU ve süreç, overlay'in konumu ve kilit durumu, tepsi tercihleri ve iki kısayol Windows sanal tuş ve değiştirici değerleri olarak bulunur.

Dosyayı silmek tüm varsayılanları geri getirir.

### Katkı

Hata bildirimleri ve pull request'ler memnuniyetle karşılanır. Lütfen dosyalardaki mevcut üsluba sadık kalın ve pull request açmadan önce `dotnet build -c Release` komutunun uyarı vermeden bittiğinden ve `dotnet test tests/Nook.Tests` testlerinin geçtiğinden emin olun. Yerel (native) koda dokunacaksanız önce [docs/architecture.md](docs/architecture.md) dosyasını okuyun.

### Lisans

[MIT](LICENSE) © 𝓐.𝓒.𝓑

[⬆ Başa dön](#top)

---

<a id="中文"></a>
## 中文（简体）

### 概述

Nook 只专注回答一个问题：某个进程此刻究竟占用了多少显存？它读取的是任务管理器所用的同一组 WDDM 性能计数器，再从显卡驱动补上温度和核心频率，结果既可以显示在一个小窗口里，也可以显示在悬浮于全屏游戏之上的透明浮层中。

没有安装程序，没有后台服务，也不向其他进程注入代码。运行一个可执行文件，用完关掉即可。

### 功能

- 任意运行中进程的专用显存与共享显存——当前值、峰值和平均值
- 每秒刷新一次的显卡负载、显存、温度和核心频率
- 通过 DXGI 枚举、按 LUID 匹配的多显卡支持
- 带鼠标穿透锁定、四角预设位置并记住位置的浮层
- 被监控的程序以新 PID 重启后自动重新绑定
- 可在系统托盘中运行，也可随 Windows 一起启动
- 完全没有任何网络请求

### 环境要求

- 64 位 Windows 10 或 11
- WDDM 2.0 及以上的显示驱动
- 温度和频率需要 NVIDIA 的 `nvml.dll` 或 AMD 的 `atiadlxx.dll`，两者都随常规驱动一起安装。缺少时，浮层中直接不显示这两行。
- 从源码构建需要 .NET 10 SDK

### 安装

从[最新发布](https://github.com/IACBI/Nook/releases/latest)下载 `Nook.exe`，或自行构建：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

产物是单个 `release\Nook.exe`，无需再安装任何依赖。

### 使用

**Process** 选项卡列出全部运行中的进程；可以按名称搜索，也可以勾选 *Only VRAM active* 隐藏从不使用 GPU 的进程。选中一项后，各项计数器就会开始更新。

**GPU** 选项卡显示所选显卡：引擎负载、专用显存与共享显存、温度和核心频率。

有两处读数需要说明。核显根本没有专用显存，笔记本上的独显在闲置时也几乎把所有内容都放在共享显存里，所以专用显存显示 0 MB 通常是准确的，而不是出了问题——浮层会跟随实际在用的那一块。温度和频率则来自显卡驱动，Intel 没有提供相应接口，因此其显卡会显示 `No driver sensor`。

浮层可以在底部工具栏操作，也可以随时用两个快捷键控制：

| 快捷键 | 作用 |
|---|---|
| `Ctrl+Shift+V` | 显示或隐藏浮层 |
| `Ctrl+Shift+L` | 在鼠标穿透与可拖动之间切换 |

处于可拖动状态时，浮层会显示 `MOVE` 标记和虚线边框；拖到合适位置后重新锁定，鼠标点击便会直接穿透到下面的游戏。

### 配置

设置保存在 `%LocalAppData%\Nook\settings.json`，在程序退出时写入。文件中包含所选显卡与进程、浮层位置与锁定状态、托盘偏好，以及以 Windows 虚拟键和修饰键数值表示的两个快捷键。

删除该文件即可恢复全部默认值。

### 参与贡献

欢迎提交 issue 和 pull request。请沿用现有代码风格，并在提交 pull request 前确认 `dotnet build -c Release` 没有任何警告、`dotnet test tests/Nook.Tests` 全部通过。若要改动原生代码，请先阅读 [docs/architecture.md](docs/architecture.md)。

### 许可证

[MIT](LICENSE) © 𝓐.𝓒.𝓑

[⬆ 回到顶部](#top)

---

<a id="हिन्दी"></a>
## हिन्दी

### परिचय

Nook एक ही सवाल का ठीक-ठीक जवाब देता है: यह प्रोसेस इस समय कितनी GPU मेमोरी इस्तेमाल कर रही है? यह वही WDDM परफ़ॉर्मेंस काउंटर पढ़ता है जो Task Manager पढ़ता है, उसमें ड्राइवर से मिला तापमान और कोर क्लॉक जोड़ता है, और नतीजा या तो एक छोटी विंडो में दिखाता है या फिर एक पारदर्शी ओवरले में जो फ़ुलस्क्रीन गेम के ऊपर टिका रहता है।

न कोई इंस्टॉलर, न बैकग्राउंड सर्विस, और न ही किसी दूसरी प्रोसेस में कोड डाला जाता है। एक ही एक्ज़ीक्यूटेबल चलाइए और काम पूरा होने पर बंद कर दीजिए।

### विशेषताएँ

- किसी भी चल रही प्रोसेस के लिए डेडिकेटेड और शेयर्ड GPU मेमोरी — मौजूदा, अधिकतम और औसत
- हर सेकंड ताज़ा होने वाला अडैप्टर लोड, मेमोरी, तापमान और कोर क्लॉक
- DXGI से सूचीबद्ध और LUID से मिलाए गए कई GPU
- क्लिक-थ्रू लॉक, कोने की तयशुदा जगहों और याद रखी गई स्थिति वाला ओवरले
- निगरानी में रखा प्रोग्राम नए PID के साथ दोबारा चलने पर अपने आप जुड़ जाना
- ट्रे से चलने की सुविधा, चाहें तो Windows के साथ शुरू होना
- किसी भी तरह का नेटवर्क ट्रैफ़िक नहीं

### आवश्यकताएँ

- 64-बिट Windows 10 या 11
- WDDM 2.0 या उससे नया डिस्प्ले ड्राइवर
- तापमान और क्लॉक के लिए NVIDIA का `nvml.dll` या AMD का `atiadlxx.dll`; दोनों सामान्य ड्राइवर के साथ ही आते हैं। इनके बिना ओवरले में वे दोनों पंक्तियाँ दिखती ही नहीं।
- सोर्स से बिल्ड करने के लिए .NET 10 SDK

### इंस्टॉलेशन

[नवीनतम रिलीज़](https://github.com/IACBI/Nook/releases/latest) से `Nook.exe` डाउनलोड कीजिए, या खुद बिल्ड कीजिए:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

नतीजा एक अकेली `release\Nook.exe` फ़ाइल है, जिसे और कुछ इंस्टॉल किए बिना चलाया जा सकता है।

### उपयोग

**Process** टैब में सब कुछ सूचीबद्ध रहता है; नाम से खोजिए, या *Only VRAM active* चुनकर उन प्रोसेस को छिपा दीजिए जो GPU छूती ही नहीं। कोई एक चुनते ही काउंटर भरने लगते हैं।

**GPU** टैब चुने हुए अडैप्टर का ब्योरा दिखाता है: इंजन लोड, डेडिकेटेड और शेयर्ड मेमोरी, तापमान और कोर क्लॉक।

दो रीडिंग समझाने लायक हैं। इंटीग्रेटेड GPU के पास डेडिकेटेड मेमोरी होती ही नहीं, और लैपटॉप का डिस्क्रीट GPU खाली बैठा हो तो लगभग सब कुछ शेयर्ड पूल में रखता है — इसलिए डेडिकेटेड का 0 MB दिखना आम तौर पर सही आँकड़ा है, खराबी नहीं; ओवरले उसी पूल को दिखाता है जो असल में इस्तेमाल हो रहा है। तापमान और क्लॉक ड्राइवर से आते हैं, और Intel ऐसा कोई इंटरफ़ेस नहीं देता, इसलिए उसके अडैप्टर `No driver sensor` बताते हैं।

ओवरले नीचे की पट्टी से, या कहीं से भी दो शॉर्टकट से नियंत्रित होता है:

| शॉर्टकट | काम |
|---|---|
| `Ctrl+Shift+V` | ओवरले दिखाएँ या छिपाएँ |
| `Ctrl+Shift+L` | क्लिक-थ्रू और खिसकाने योग्य के बीच बदलें |

खिसकाने योग्य होने पर ओवरले `MOVE` बैज और डैश वाली किनार दिखाता है; उसे मनचाही जगह ले जाकर फिर लॉक कर दीजिए, ताकि क्लिक सीधे नीचे चल रहे गेम तक पहुँचें।

### कॉन्फ़िगरेशन

सेटिंग्स `%LocalAppData%\Nook\settings.json` में रहती हैं और ऐप्लिकेशन बंद होते समय लिखी जाती हैं। फ़ाइल में चुना हुआ GPU और प्रोसेस, ओवरले की जगह और लॉक की स्थिति, ट्रे से जुड़ी पसंद, और दोनों हॉटकी Windows वर्चुअल-की तथा मॉडिफ़ायर मानों के रूप में रहती हैं।

फ़ाइल हटा देने पर सभी डिफ़ॉल्ट लौट आते हैं।

### योगदान

इशू और पुल रिक्वेस्ट का स्वागत है। कृपया फ़ाइलों में पहले से मौजूद शैली बनाए रखें, और पुल रिक्वेस्ट खोलने से पहले देख लें कि `dotnet build -c Release` बिना किसी चेतावनी के पूरा होता है और `dotnet test tests/Nook.Tests` पास होता है। नेटिव कोड में बदलाव करना हो तो पहले [docs/architecture.md](docs/architecture.md) पढ़ लें।

### लाइसेंस

[MIT](LICENSE) © 𝓐.𝓒.𝓑

[⬆ ऊपर जाएँ](#top)

---

<a id="español"></a>
## Español

### Descripción general

Nook responde bien a una sola pregunta: ¿cuánta memoria de vídeo está usando este proceso ahora mismo? Lee los mismos contadores de rendimiento WDDM que consulta el Administrador de tareas, les suma la temperatura y la frecuencia del núcleo que expone el controlador, y muestra el resultado en una ventana pequeña o en una superposición transparente que se mantiene sobre los juegos a pantalla completa.

No hay instalador, ni servicio en segundo plano, ni código inyectado en otros procesos. Se ejecuta un único archivo y se cierra al terminar.

### Características

- Memoria de GPU dedicada y compartida de cualquier proceso en ejecución: actual, máxima y media
- Carga del adaptador, memoria, temperatura y frecuencia del núcleo, actualizadas cada segundo
- Varias GPU, enumeradas mediante DXGI y emparejadas por LUID
- Superposición con bloqueo de clic pasante, posiciones de esquina y posición recordada
- Reconexión automática cuando el programa vigilado se reinicia con otro PID
- Funcionamiento desde la bandeja y, si se quiere, arranque con Windows
- Ningún tipo de tráfico de red

### Requisitos

- Windows 10 u 11 de 64 bits
- Un controlador de pantalla WDDM 2.0 o posterior
- Para la temperatura y la frecuencia: `nvml.dll` de NVIDIA o `atiadlxx.dll` de AMD, que se instalan con el controlador habitual. Sin ellos, esas dos líneas simplemente no aparecen en la superposición.
- .NET 10 SDK para compilar desde el código fuente

### Instalación

Descarga `Nook.exe` desde la [última versión](https://github.com/IACBI/Nook/releases/latest) o compílalo tú mismo:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

El resultado es un único `release\Nook.exe` que no necesita nada más instalado.

### Uso

La pestaña **Process** enumera todo lo que se está ejecutando; busca por nombre o marca *Only VRAM active* para ocultar los procesos que nunca tocan la GPU. Al elegir uno, los contadores empiezan a llenarse.

La pestaña **GPU** muestra el adaptador seleccionado: carga del motor, memoria dedicada y compartida, temperatura y frecuencia del núcleo.

Conviene explicar dos lecturas. Las GPU integradas no tienen memoria dedicada, y la GPU discreta de un portátil deja casi todo en el grupo compartido mientras está en reposo, así que un valor dedicado de 0 MB suele ser correcto y no un fallo: la superposición sigue al grupo que realmente se está usando. La temperatura y la frecuencia proceden del controlador, e Intel no expone ninguna API para ello, por lo que sus adaptadores muestran `No driver sensor`.

La superposición se controla desde la barra inferior o, desde cualquier sitio, con dos atajos:

| Atajo | Acción |
|---|---|
| `Ctrl+Shift+V` | Mostrar u ocultar la superposición |
| `Ctrl+Shift+L` | Alternar entre clic pasante y arrastrable |

Mientras es arrastrable, la superposición muestra la etiqueta `MOVE` y un borde discontinuo; llévala donde quieras y vuelve a bloquearla para que los clics pasen directamente al juego que hay debajo.

### Configuración

Los ajustes se guardan en `%LocalAppData%\Nook\settings.json` y se escriben al cerrar la aplicación. El archivo contiene la GPU y el proceso seleccionados, la posición y el estado de bloqueo de la superposición, las preferencias de la bandeja y los dos atajos como valores de tecla virtual y modificador de Windows.

Al borrar el archivo se restauran todos los valores predeterminados.

### Contribuir

Las incidencias y los pull requests son bienvenidos. Mantén el estilo que ya tienen los archivos y comprueba que `dotnet build -c Release` termina sin advertencias y que `dotnet test tests/Nook.Tests` pasa antes de abrir un pull request. Si vas a tocar el código nativo, lee antes [docs/architecture.md](docs/architecture.md).

### Licencia

[MIT](LICENSE) © 𝓐.𝓒.𝓑

[⬆ Volver arriba](#top)

---

<a id="العربية"></a>
## العربية

<div dir="rtl" align="right">

### نظرة عامة

يجيب Nook عن سؤال واحد بدقة: كم مقدار ذاكرة الرسوميات التي تستهلكها هذه العملية الآن؟ يقرأ البرنامج عدّادات أداء WDDM نفسها التي يقرأها مدير المهام، ويضيف إليها درجة الحرارة وتردد النواة من تعريف كرت الشاشة، ثم يعرض النتيجة إمّا في نافذة صغيرة وإمّا في طبقة شفافة تبقى فوق الألعاب في وضع ملء الشاشة.

لا يوجد مثبّت ولا خدمة تعمل في الخلفية ولا حقن للشيفرة في عمليات أخرى. تشغّل ملفًا تنفيذيًا واحدًا ثم تغلقه عند الانتهاء.

### المزايا

- ذاكرة الرسوميات المخصّصة والمشتركة لأي عملية قيد التشغيل: القيمة الحالية والقصوى والمتوسطة
- حِمل المحوِّل والذاكرة ودرجة الحرارة وتردد النواة، بتحديث كل ثانية
- دعم عدة كروت شاشة، تُعدّد عبر DXGI وتُطابَق بمعرّف LUID
- طبقة شفافة بقفل يمرّر النقر، ومواضع جاهزة في الزوايا، مع تذكّر آخر موضع
- إعادة ارتباط تلقائية عندما يُعاد تشغيل البرنامج المراقَب بمعرّف عملية جديد
- التشغيل من شريط النظام، مع إمكانية البدء مع Windows
- لا اتصال بالشبكة على الإطلاق

### المتطلبات

- نظام Windows 10 أو 11 بنسخة 64-بت
- تعريف عرض بمواصفة WDDM 2.0 أو أحدث
- لقراءة الحرارة والتردد: ملف `nvml.dll` من NVIDIA أو `atiadlxx.dll` من AMD، وكلاهما يُثبَّت مع التعريف المعتاد. وفي غيابهما لا يظهر هذان السطران في الطبقة الشفافة أصلًا.
- حزمة .NET 10 SDK للبناء من المصدر

### التثبيت

نزّل ملف `Nook.exe` من [أحدث إصدار](https://github.com/IACBI/Nook/releases/latest)، أو ابْنِه بنفسك:

<div dir="ltr" align="left">

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

</div>

الناتج ملف واحد هو `release\Nook.exe` لا يحتاج إلى تثبيت أي شيء آخر.

### الاستخدام

يعرض تبويب **Process** كل ما يعمل على الجهاز؛ ابحث بالاسم، أو فعّل خيار *Only VRAM active* لإخفاء العمليات التي لا تستخدم كرت الشاشة إطلاقًا. وبمجرد اختيار عملية تبدأ العدّادات بالامتلاء.

ويعرض تبويب **GPU** بيانات المحوِّل المحدَّد: حِمل المحرّك، والذاكرة المخصّصة والمشتركة، ودرجة الحرارة، وتردد النواة.

وهناك قراءتان تستحقان التوضيح. كروت الرسوميات المدمجة لا تملك ذاكرة مخصّصة أصلًا، كما أن الكرت المنفصل في الحواسيب المحمولة يُبقي معظم بياناته في الذاكرة المشتركة ما دام خاملًا؛ لذا فإن ظهور 0 ميغابايت في الذاكرة المخصّصة قراءة صحيحة غالبًا وليس عطلًا، والطبقة الشفافة تتبع الذاكرة المستخدمة فعليًا. أما الحرارة والتردد فمصدرهما تعريف كرت الشاشة، وإنتل لا توفّر واجهة لذلك، فتظهر لمحوّلاتها عبارة `No driver sensor`.

يمكن التحكم بالطبقة الشفافة من الشريط السفلي أو من أي مكان عبر اختصارين:

| الاختصار | الوظيفة |
|---|---|
| `Ctrl+Shift+V` | إظهار الطبقة الشفافة أو إخفاؤها |
| `Ctrl+Shift+L` | التبديل بين تمرير النقر والسحب |

في وضع السحب تظهر شارة `MOVE` وإطار متقطّع؛ انقل الطبقة حيث تشاء ثم أعِد قفلها لتنفذ النقرات مباشرة إلى اللعبة تحتها.

### الإعدادات

تُحفظ الإعدادات في `%LocalAppData%\Nook\settings.json` وتُكتب عند إغلاق البرنامج. يحتوي الملف على كرت الشاشة والعملية المحدَّدين، وموضع الطبقة الشفافة وحالة قفلها، وتفضيلات شريط النظام، والاختصارين بصيغة رموز المفاتيح الافتراضية ومفاتيح التعديل في Windows.

وحذف الملف يعيد كل القيم الافتراضية.

### المساهمة

المساهمات وطلبات الدمج مُرحَّب بها. التزم بالأسلوب الموجود في الملفات، وتأكد من أن الأمر `dotnet build -c Release` ينتهي دون أي تحذير وأن `dotnet test tests/Nook.Tests` ينجح قبل فتح طلب الدمج. وإن كنت ستعدّل الشيفرة الأصلية (native) فاقرأ أولًا ملف [docs/architecture.md](docs/architecture.md).

### الرخصة

[MIT](LICENSE) © 𝓐.𝓒.𝓑

</div>

[⬆ العودة إلى الأعلى](#top)

---

<a id="português"></a>
## Português (Brasil)

### Visão geral

O Nook responde bem a uma única pergunta: quanta memória de vídeo este processo está usando agora? Ele lê os mesmos contadores de desempenho WDDM que o Gerenciador de Tarefas consulta, acrescenta temperatura e clock do núcleo vindos do driver e mostra o resultado em uma janela pequena ou em uma sobreposição transparente que fica acima de jogos em tela cheia.

Não há instalador, serviço em segundo plano nem código injetado em outros processos. Você executa um único arquivo e fecha quando terminar.

### Recursos

- Memória de GPU dedicada e compartilhada de qualquer processo em execução: atual, máxima e média
- Carga do adaptador, memória, temperatura e clock do núcleo, atualizados a cada segundo
- Várias GPUs, enumeradas via DXGI e correspondidas por LUID
- Sobreposição com trava de clique passante, posições de canto e posição memorizada
- Reconexão automática quando o programa monitorado reinicia com outro PID
- Operação pela bandeja e, se quiser, início junto com o Windows
- Nenhum tráfego de rede

### Requisitos

- Windows 10 ou 11 de 64 bits
- Driver de vídeo WDDM 2.0 ou mais recente
- Para temperatura e clock: `nvml.dll` da NVIDIA ou `atiadlxx.dll` da AMD, ambos instalados junto com o driver comum. Sem eles, essas duas linhas simplesmente não aparecem na sobreposição.
- .NET 10 SDK para compilar a partir do código-fonte

### Instalação

Baixe o `Nook.exe` na [versão mais recente](https://github.com/IACBI/Nook/releases/latest) ou compile você mesmo:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

O resultado é um único `release\Nook.exe`, que não exige mais nada instalado.

### Uso

A aba **Process** lista tudo o que está em execução; pesquise pelo nome ou marque *Only VRAM active* para esconder os processos que nunca tocam a GPU. Ao escolher um deles, os contadores começam a se preencher.

A aba **GPU** mostra o adaptador selecionado: carga do motor, memória dedicada e compartilhada, temperatura e clock do núcleo.

Duas leituras merecem explicação. GPUs integradas não têm memória dedicada, e a GPU dedicada de um notebook mantém quase tudo no grupo compartilhado enquanto está ociosa, então 0 MB de memória dedicada costuma ser um número correto, não um defeito — a sobreposição acompanha o grupo que está realmente em uso. Já temperatura e clock vêm do driver, e a Intel não expõe nenhuma API para isso, de modo que seus adaptadores mostram `No driver sensor`.

A sobreposição é controlada pela barra inferior ou, de qualquer lugar, por dois atalhos:

| Atalho | Ação |
|---|---|
| `Ctrl+Shift+V` | Mostrar ou ocultar a sobreposição |
| `Ctrl+Shift+L` | Alternar entre clique passante e arrastável |

Enquanto está arrastável, a sobreposição exibe o selo `MOVE` e uma borda tracejada; leve-a para onde quiser e trave de novo, para que os cliques passem direto ao jogo que está embaixo.

### Configuração

As configurações ficam em `%LocalAppData%\Nook\settings.json` e são gravadas ao encerrar o aplicativo. O arquivo guarda a GPU e o processo selecionados, a posição e o estado de trava da sobreposição, as preferências da bandeja e os dois atalhos como valores de tecla virtual e modificador do Windows.

Apagar o arquivo restaura todos os padrões.

### Contribuindo

Issues e pull requests são bem-vindos. Mantenha o estilo que já existe nos arquivos e confirme que `dotnet build -c Release` termina sem avisos e que `dotnet test tests/Nook.Tests` passa antes de abrir um pull request. Se for mexer no código nativo, leia antes [docs/architecture.md](docs/architecture.md).

### Licença

[MIT](LICENSE) © 𝓐.𝓒.𝓑

[⬆ Voltar ao topo](#top)

---

<a id="русский"></a>
## Русский

### Обзор

Nook хорошо отвечает на один вопрос: сколько видеопамяти этот процесс занимает прямо сейчас? Программа читает те же счётчики производительности WDDM, что и диспетчер задач, добавляет к ним температуру и частоту ядра из драйвера и показывает результат либо в небольшом окне, либо в прозрачном оверлее поверх полноэкранных игр.

Ни установщика, ни фоновой службы, ни внедрения кода в чужие процессы. Запускаете один исполняемый файл и закрываете, когда он больше не нужен.

### Возможности

- Выделенная и общая видеопамять любого работающего процесса: текущая, пиковая и средняя
- Нагрузка адаптера, память, температура и частота ядра с обновлением раз в секунду
- Несколько видеокарт: перечисление через DXGI и сопоставление по LUID
- Оверлей с блокировкой сквозных кликов, углами по умолчанию и сохранением позиции
- Автоматическое переподключение, когда отслеживаемая программа перезапускается с новым PID
- Работа из системного лотка и, при желании, запуск вместе с Windows
- Никакого сетевого трафика

### Требования

- 64-разрядная Windows 10 или 11
- Драйвер дисплея WDDM 2.0 или новее
- Для температуры и частоты: `nvml.dll` от NVIDIA или `atiadlxx.dll` от AMD — обе библиотеки ставятся вместе с обычным драйвером. Без них эти две строки в оверлее просто не выводятся.
- .NET 10 SDK для сборки из исходников

### Установка

Скачайте `Nook.exe` из [последнего релиза](https://github.com/IACBI/Nook/releases/latest) или соберите сами:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o release
```

Результат — один файл `release\Nook.exe`, которому больше ничего не требуется.

### Использование

Вкладка **Process** перечисляет всё запущенное; ищите по имени или включите *Only VRAM active*, чтобы скрыть процессы, которые вообще не обращаются к видеокарте. Как только процесс выбран, счётчики начинают заполняться.

Вкладка **GPU** показывает выбранный адаптер: нагрузку движка, выделенную и общую память, температуру и частоту ядра.

Два показателя стоит пояснить. У встроенных видеокарт выделенной памяти нет вовсе, а дискретная видеокарта ноутбука в простое держит почти всё в общей памяти, поэтому 0 МБ выделенной памяти — обычно верное значение, а не сбой: оверлей показывает ту память, которая действительно используется. Температура и частота приходят из драйвера, а Intel такого интерфейса не предоставляет, поэтому её адаптеры показывают `No driver sensor`.

Оверлеем управляют из нижней панели или откуда угодно двумя сочетаниями клавиш:

| Сочетание | Действие |
|---|---|
| `Ctrl+Shift+V` | Показать или скрыть оверлей |
| `Ctrl+Shift+L` | Переключить сквозные клики и перетаскивание |

В режиме перетаскивания оверлей показывает метку `MOVE` и пунктирную рамку; перенесите его куда нужно и снова заблокируйте, чтобы клики уходили прямо в игру под ним.

### Настройки

Настройки лежат в `%LocalAppData%\Nook\settings.json` и записываются при выходе из программы. В файле хранятся выбранные видеокарта и процесс, позиция и состояние блокировки оверлея, параметры работы в лотке и оба сочетания клавиш в виде кодов виртуальных клавиш и модификаторов Windows.

Удаление файла возвращает все значения по умолчанию.

### Участие в разработке

Issue и pull request приветствуются. Придерживайтесь стиля, который уже есть в файлах, и убедитесь, что `dotnet build -c Release` завершается без предупреждений, а `dotnet test tests/Nook.Tests` проходит, прежде чем открывать pull request. Если правите нативный код, сначала прочитайте [docs/architecture.md](docs/architecture.md).

### Лицензия

[MIT](LICENSE) © 𝓐.𝓒.𝓑

[⬆ Наверх](#top)
