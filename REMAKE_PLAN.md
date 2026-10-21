# QuickStartLauncher v3 — Kế hoạch Remake

> Tài liệu triển khai chi tiết cho bản remake. Làm **ngay tại thư mục này** (`D:\Quyetnm\Dev\Auto Open\QuickStartLauncher`).
> Mục tiêu: bật máy → app tự (hoặc 1 click) mở toàn bộ ứng dụng cần thiết; UI glassmorphism đẹp, chọn app từ danh sách Windows thay vì gõ đường dẫn.

---

## 0. Quyết định đã chốt

| # | Quyết định |
|---|---|
| Stack | .NET 8 + WPF, viết lại sạch trong project hiện tại |
| UI lib | **WPF-UI** (lepoco/wpfui) cho FluentWindow + backdrop Acrylic, style glass tự viết đè lên |
| MVVM | **CommunityToolkit.Mvvm** (bỏ `ViewModelBase`/`RelayCommand` tự viết) |
| Nguồn app | **`shell:AppsFolder`** (đúng danh sách Start Menu, gồm Win32 + Store/UWP) + Browse `.exe` fallback |
| Startup | 3 chế độ: **Không** / **Chạy tự động profile đã chọn** / **Chỉ mở Launcher**; mỗi action có cờ `skipOnAutostart` |
| Profiles | Giữ nhiều danh sách như cũ; thêm duplicate, export/import |
| Config cũ | Tự migrate v2 → v3, backup file cũ |

---

## 1. Hiện trạng (bản v2) — để biết cái gì bỏ/giữ

- Code: `App.xaml(.cs)`, `MainWindow`, `Views/ConfigWindow`, `ViewModels/{Main,Config}ViewModel`, `Models/{ActionBase,AppAction,CommandAction,Profile,AppConfig,IndexedApp}`, `Services/{Config,Log,ActionExecutor,AppIndex}Service`, `Core/*` (converter, VM base).
- Chỉ hỗ trợ action `app` + `command`; `website`/`delay` chưa có trong executor.
- `AppIndexService` chưa resolve `.lnk`, không có fuzzy, UI không dùng.
- `ConfigViewModel` tự đổi loại action khi Path không đuôi `.exe` → bug.
- JSON v2 bị trùng discriminator (`$type` + `type` int).
- Config thật của user (`%LocalAppData%\QuickStart\config\config.json`) — profile `Default`:
  1. Chrome → `https://www.facebook.com/`
  2. `C:\Users\zeusa\AppData\Local\Programs\Zalo\Zalo.exe`
  3. command `start msteams:`
  4. `C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE`

**Giữ**: Serilog, vị trí lưu `%LocalAppData%\QuickStart\`, icon `Assets\APP.ico`, khái niệm profile.
**Bỏ/viết lại**: toàn bộ Views, ViewModels, Models, Services, Core.

---

## 2. Packages

```xml
<PackageReference Include="WPF-UI" Version="(pin bản stable mới nhất khi làm)" />
<PackageReference Include="CommunityToolkit.Mvvm" Version="(pin)" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.*" />          <!-- optional: host + DI -->
<PackageReference Include="Serilog" Version="(nâng cấp)" />
<PackageReference Include="Serilog.Sinks.File" Version="(nâng cấp)" />
<PackageReference Include="gong-wpf-dragdrop" Version="(pin)" />                 <!-- kéo thả reorder -->
<PackageReference Include="Microsoft.Windows.CsWin32" Version="(pin)" PrivateAssets="all" />  <!-- P/Invoke + COM shell -->
```

- `csproj`: `TargetFramework` = `net8.0-windows10.0.19041.0` (cần cho API Windows mới), `SupportedOSPlatformVersion` = `10.0.19041.0`, `UseWPF`, `Nullable`, `ImplicitUsings`.
- Release: `PublishSingleFile=true`, `SelfContained=false` (hoặc true nếu muốn chạy máy không có runtime), `RuntimeIdentifier=win-x64`.
- CsWin32: tạo `NativeMethods.txt` liệt kê API cần (xem §5.1, §6.2). Nếu CsWin32 gặp khó với COM shell → fallback gói `Microsoft-WindowsAPICodePack-Shell` (`KnownFolders.AppsFolder`).

---

## 3. Cấu trúc thư mục mới

```
QuickStartLauncher/
├─ App.xaml / App.xaml.cs            # bootstrap DI, parse args, single-instance
├─ Assets/APP.ico
├─ Themes/
│  ├─ Glass.Colors.xaml              # design tokens (light/dark)
│  ├─ Glass.Controls.xaml            # GlassCard, GlassButton, PrimaryButton, TextBox, Toggle, Chip…
│  └─ Glass.Animations.xaml
├─ Views/
│  ├─ LauncherWindow.xaml            # popup chính
│  ├─ AutostartToast.xaml            # toast đếm ngược khi khởi động cùng Windows
│  ├─ ConfigWindow.xaml              # shell: sidebar profile + nội dung
│  ├─ Pages/ActionsPage.xaml
│  ├─ Pages/SettingsPage.xaml
│  ├─ Dialogs/AppPickerDialog.xaml
│  └─ Editors/{App,Website,Command,Delay}ActionEditor.xaml
├─ ViewModels/
│  ├─ LauncherViewModel.cs
│  ├─ AutostartToastViewModel.cs
│  ├─ ConfigViewModel.cs
│  ├─ ProfileItemViewModel.cs
│  ├─ ActionItemViewModel.cs (+ các editor VM)
│  ├─ AppPickerViewModel.cs
│  └─ SettingsViewModel.cs
├─ Models/
│  ├─ AppConfig.cs, Profile.cs, AppSettings.cs
│  ├─ Actions/{LaunchAction,AppAction,WebsiteAction,CommandAction,DelayAction,BrowserOptions}.cs
│  ├─ InstalledApp.cs, BrowserInfo.cs, BrowserProfile.cs
│  └─ RunResult.cs
├─ Services/
│  ├─ Config/   IConfigService, ConfigService, ConfigMigrator
│  ├─ Apps/     IInstalledAppsService, ShellAppsFolderProvider, IconCache
│  ├─ Search/   FuzzySearch, TextNormalizer
│  ├─ Browsers/ IBrowserService, BrowserCatalog, BrowserProfileReader
│  ├─ Launch/   ILaunchService, LaunchService (+ handler mỗi loại action)
│  ├─ Startup/  IStartupService, StartupService
│  └─ Logging/  Serilog setup
├─ Interop/ NativeMethods.txt, ShellInterop.cs, DwmInterop.cs
├─ Converters/
├─ NativeMethods.txt
├─ README.md, spec.md (cập nhật v3), REMAKE_PLAN.md
```

---

## 4. Data model & JSON v3

### 4.1 Schema

```jsonc
{
  "version": 3,
  "settings": {
    "theme": "system",                 // system | light | dark
    "startup": {
      "mode": "off",                   // off | runProfiles | showLauncher
      "profileIds": ["<guid>"],        // dùng khi mode = runProfiles (1..n profile, chạy theo thứ tự)
      "delaySeconds": 10,              // trễ sau khi logon trước khi bắt đầu
      "countdownSeconds": 5            // toast đếm ngược cho phép Hủy / Mở ngay
    },
    "closeWhenDone": true,             // Launcher tự thoát sau khi chạy xong
    "closeDelayMs": 1200,              // giữ màn kết quả 1.2s rồi thoát (nếu 0 lỗi)
    "keepOpenOnError": true            // có lỗi thì không tự thoát
  },
  "activeProfileId": "<guid>",
  "profiles": [
    {
      "id": "<guid>",
      "name": "Làm việc",
      "icon": "💼",                    // emoji/glyph tùy chọn
      "actions": [
        {
          "kind": "app",
          "id": "<guid>",
          "name": "Google Chrome",
          "enabled": true,
          "skipOnAutostart": false,
          "delayAfterMs": 0,
          "target": {
            "aumid": "Chrome",                     // ưu tiên nếu có
            "path": "C:\\...\\chrome.exe"         // fallback / app chọn qua Browse
          },
          "arguments": "",
          "workingDir": null,
          "runAsAdmin": false,
          "browser": {                             // chỉ có khi app là trình duyệt
            "urls": ["https://www.facebook.com/", "https://mail.google.com/"],
            "newWindow": true,
            "profileDir": "Profile 1"              // Chromium: tên thư mục; Firefox: tên profile
          }
        },
        { "kind": "website", "id": "…", "name": "Portal", "enabled": true, "skipOnAutostart": false, "delayAfterMs": 0,
          "url": "https://portal.company", "browserId": "default", "newWindow": false },
        { "kind": "command", "id": "…", "name": "Teams", "enabled": true, "skipOnAutostart": false, "delayAfterMs": 0,
          "command": "start msteams:", "shell": "cmd", "workingDir": null, "hidden": true, "runAsAdmin": false },
        { "kind": "delay", "id": "…", "enabled": true, "skipOnAutostart": false, "ms": 1500 }
      ]
    }
  ]
}
```

### 4.2 C#

- `LaunchAction` (abstract) + `[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]` + `[JsonDerivedType(typeof(AppAction), "app")]`… → **chỉ 1 discriminator**.
- Model là POCO thuần (không INotifyPropertyChanged). UI bind qua `ActionItemViewModel` (ObservableObject) bọc model → tránh lẫn logic UI vào file JSON.
- `JsonSerializerOptions`: camelCase, `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`, `WriteIndented`, `DefaultIgnoreCondition = WhenWritingNull`.
- Profile/action dùng `Guid id` để profile đổi tên không làm hỏng `startup.profileIds`.

### 4.3 Lưu trữ

| File | Đường dẫn |
|---|---|
| Config | `%LocalAppData%\QuickStart\config\config.json` |
| Backup migrate | `%LocalAppData%\QuickStart\config\config.v2.bak.json` |
| Cache app | `%LocalAppData%\QuickStart\cache\apps.json` |
| Cache icon | `%LocalAppData%\QuickStart\cache\icons\<sha1(key)>.png` |
| Log | `%LocalAppData%\QuickStart\logs\yyyy-MM-dd.log` (giữ 14 ngày) |

- Ghi config **atomic**: ghi `config.json.tmp` → `File.Replace` (hoặc `File.Move(overwrite:true)`), tránh hỏng file khi tắt máy giữa chừng.
- Portable (tùy chọn): nếu cạnh exe có file `portable.flag` → lưu mọi thứ cạnh exe.

### 4.4 Migration v2 → v3 (`ConfigMigrator`)

Nhận diện v2: không có `version` và action có `$type`.

| v2 | v3 |
|---|---|
| `$type: "app"`, `path`, `url`, `profile`, `newWindow`, `arguments`, `runAsAdmin` | `kind: app`, `target.path`, `name` = FileVersionInfo.FileDescription hoặc tên file; nếu exe là browser → `browser { urls: [url] nếu không rỗng, newWindow, profileDir: profile }` |
| `$type: "command"`, `command`, `shell` (0=cmd,1=powershell), `workingDir`, `runAsAdmin` | `kind: command`, `shell: cmd/powershell`, `hidden: true` |
| `$type: "website"` / `delay` (nếu có trong file mẫu) | map tương ứng |
| `activeProfile` (tên) | `activeProfileId` (tra theo tên) |
| — | `settings` mặc định, `startup.mode = off` |

- Sau migrate: thử resolve `target.path` sang AUMID trong danh sách AppsFolder (khớp đường dẫn exe) → điền `aumid` để có icon/tên đẹp.
- Backup file gốc trước khi ghi đè. Log rõ từng bước. Migrate lỗi → giữ file cũ, mở app với config rỗng + banner cảnh báo.
- **Test bắt buộc**: migrate đúng 4 action của config thật hiện tại (§1).

---

## 5. Services

### 5.1 `IInstalledAppsService` — danh sách ứng dụng Windows

```csharp
public sealed record InstalledApp(
    string Key,          // AUMID hoặc parsing name — khóa duy nhất
    string Name,         // tên hiển thị
    string? Aumid,
    string? ExePath,     // nếu resolve được (Win32)
    bool IsPackaged,     // Store/UWP
    string? BrowserId);  // != null nếu là trình duyệt (xem §5.3)

Task<IReadOnlyList<InstalledApp>> GetAppsAsync(bool forceRefresh = false);
Task<ImageSource?> GetIconAsync(InstalledApp app, int size = 48);
```

**Cách enumerate `shell:AppsFolder`** (chạy trên thread STA riêng, vì COM shell):
1. `SHGetKnownFolderItem(FOLDERID_AppsFolder, KF_FLAG_DEFAULT, null, IID_IShellItem)`.
2. `BindToHandler(BHID_EnumItems, IID_IEnumShellItems)` → duyệt từng `IShellItem`.
3. Với mỗi item (`IShellItem2`):
   - Tên: `GetDisplayName(SIGDN_NORMALDISPLAY)`.
   - AUMID: `GetString(PKEY_AppUserModel_ID)`; nếu rỗng dùng `GetDisplayName(SIGDN_PARENTRELATIVEPARSING)`.
   - Exe path: `GetString(PKEY_Link_TargetParsingPath)`; nếu parsing name dạng `{KNOWNFOLDERID}\sub\app.exe` → resolve known folder bằng `SHGetKnownFolderPath`.
   - Packaged: AUMID chứa `!` và `_` (dạng `PackageFamilyName!AppId`) hoặc `PKEY_AppUserModel_IsDualMode`/package props.
4. Lọc rác: bỏ item tên chứa `uninstall`, `gỡ cài đặt`, `readme`, `help`, `documentation`, `website`, file đích `.url/.chm/.txt/.pdf/.html`.
5. Dedupe theo `ExePath` (ưu tiên item có AUMID).
6. Sort theo tên (culture vi-VN).

**Icon**: `IShellItemImageFactory.GetImage(size, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK)` → HBITMAP → `Imaging.CreateBitmapSourceFromHBitmap` → `DeleteObject`. `Freeze()` bitmap. Cache PNG trên đĩa + `ConcurrentDictionary` trong RAM. Với action chọn qua Browse `.exe`: `SHCreateItemFromParsingName(path)` rồi dùng cùng factory.

**Cache & refresh**: load `apps.json` ngay (hiển thị tức thì) → refresh nền mỗi lần mở App Picker → diff & cập nhật list. Mục tiêu: enumerate ~300 app ≤ 1.5s, mở picker có dữ liệu ≤ 200ms.

`NativeMethods.txt` (CsWin32) gợi ý:
```
SHGetKnownFolderItem
SHGetKnownFolderPath
SHCreateItemFromParsingName
IShellItem2
IEnumShellItems
IShellItemImageFactory
IApplicationActivationManager
ApplicationActivationManager
DeleteObject
DwmSetWindowAttribute
FOLDERID_AppsFolder
BHID_EnumItems
PKEY_AppUserModel_ID
PKEY_Link_TargetParsingPath
```

### 5.2 Fuzzy search (`TextNormalizer`, `FuzzySearch`)

- Normalize: lowercase → `Normalize(FormD)` bỏ `NonSpacingMark` → `đ→d` → bỏ ký tự không phải chữ/số → gộp khoảng trắng.
- Alias mỗi app: tên hiển thị, tên file exe (không đuôi), chữ cái đầu mỗi từ (`Visual Studio Code` → `vsc`).
- Score (cao → thấp): exact (100) > prefix (90) > prefix theo từ (80) > acronym (75) > substring (60) > fuzzy: Damerau-Levenshtein trên token, `score = 50 * (1 - dist/len)` chỉ nhận `dist ≤ max(1, len/3)`.
- Debounce 120ms khi gõ; tìm trên list đã normalize sẵn (pre-compute khi load).
- Acceptance: `chrmoe` → Google Chrome top 3; `zalo`, `teams`, `outlok` → đúng app; `coc coc`/`coccoc` → Cốc Cốc.

### 5.3 `IBrowserService` — nhận diện trình duyệt & profile

`BrowserCatalog` (bảng tĩnh):

| BrowserId | Exe | AUMID gợi ý | User Data (Chromium) | Args mở URL |
|---|---|---|---|---|
| `chrome` | `chrome.exe` | `Chrome` | `%LocalAppData%\Google\Chrome\User Data` | `--profile-directory="X" [--new-window] url1 url2` |
| `edge` | `msedge.exe` | `MSEdge` | `%LocalAppData%\Microsoft\Edge\User Data` | như Chromium |
| `brave` | `brave.exe` | `Brave` | `%LocalAppData%\BraveSoftware\Brave-Browser\User Data` | như Chromium |
| `coccoc` | `browser.exe` (thư mục `CocCoc\Browser`) | — | `%LocalAppData%\CocCoc\Browser\User Data` | như Chromium |
| `opera` | `opera.exe`/`launcher.exe` | — | `%AppData%\Opera Software\Opera Stable` (không chọn profile) | `[--new-window] url…` |
| `firefox` | `firefox.exe` | `308046B0AF4A39CB` | `%AppData%\Mozilla\Firefox\profiles.ini` | `-P "name" [-new-window url] [-new-tab url]…` |

- Nhận diện: so `Path.GetFileName(exePath)` + kiểm tra thư mục cha (để `browser.exe` chỉ khớp Cốc Cốc), hoặc AUMID khớp bảng. Double-check `FileVersionInfo.ProductName`.
- **Đọc profile Chromium**: parse `Local State` (JSON) → `profile.info_cache` → mỗi key là `profileDir` (`Default`, `Profile 1`…), lấy `name` (tên hiển thị), `user_name` (email) nếu có. Mở file với `FileShare.ReadWrite` (browser đang chạy vẫn đọc được).
- **Firefox**: parse `profiles.ini` → `Name=`.
- **Browser mặc định**: đọc `HKCU\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice\ProgId` → map ProgId (`ChromeHTML`, `MSEdgeHTM`, `FirefoxURL-…`, `BraveHTML`, `CocCocHTML`…). Nếu không map được → `UseShellExecute` với URL.
- Lưu ý Chrome: nếu Chrome đã chạy, `--profile-directory` vẫn mở đúng profile; `--new-window` mở cửa sổ mới chứa mọi URL dạng tab.

### 5.4 `ILaunchService` — thực thi

```csharp
IAsyncEnumerable<RunProgress> RunAsync(Profile profile, RunContext ctx, CancellationToken ct);
Task<RunResult> RunSingleAsync(LaunchAction action, CancellationToken ct);   // nút "Chạy thử"
```

- `RunContext.IsAutostart` → bỏ qua action có `skipOnAutostart`. Luôn bỏ qua `enabled=false`.
- Chạy **tuần tự**, sau mỗi action chờ `delayAfterMs`. Lỗi 1 action không dừng queue. Hủy được qua `CancellationToken`.
- Mỗi action trả `RunProgress { ActionId, State: Pending|Running|Done|Failed|Skipped, Error? }` để UI cập nhật.

**AppAction**:
1. Nếu có `browser` → build args theo §5.3 (URL được quote, validate `Uri.TryCreate` scheme http/https/file).
2. Có `ExePath` (resolve từ `target.path` hoặc từ AUMID lúc chạy) → `ProcessStartInfo { FileName = exe, Arguments, UseShellExecute = true, WorkingDirectory, Verb = runAsAdmin ? "runas" : null }`.
3. Packaged app (Store/UWP) → `IApplicationActivationManager.ActivateApplication(aumid, args, AO_NONE, out pid)`.
4. Fallback cuối: `explorer.exe "shell:AppsFolder\<aumid>"` (không truyền được args).
5. `runAsAdmin` + user bấm "No" ở UAC → `Win32Exception 1223` → state `Skipped` (không tính là lỗi nặng), log WARN.

**WebsiteAction**: `browserId = default` → `Process.Start(new PSI(url){UseShellExecute=true})`; khác → tìm exe trình duyệt đó qua `IBrowserService` và gọi như AppAction có `browser.urls=[url]`.

**CommandAction**:
- `cmd`: `cmd.exe /d /c <command>`; `powershell`: `powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "<command>"` (nếu có `pwsh` thì cho chọn).
- `hidden` → `CreateNoWindow = true`, `UseShellExecute = false`, `WindowStyle = Hidden` (không áp dụng được khi `runAsAdmin` — khi đó buộc UseShellExecute=true, ghi chú trên UI).
- Không chờ process kết thúc (fire-and-forget), nhưng chờ `Process.Start` trả về. Chỉ `WaitForExit` nếu sau này thêm cờ `waitForExit`.

**DelayAction**: `Task.Delay(ms, ct)`; UI hiển thị đếm ngược.

### 5.5 `IStartupService` — khởi động cùng Windows

- Đăng ký: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `QuickStartLauncher` = `"<đường dẫn exe>" --autostart`. Không cần admin.
- `Apply(StartupSettings)`: mode `off` → xóa value; mode khác → ghi value (cập nhật path nếu exe đã di chuyển). Gọi mỗi lần Save settings **và** mỗi lần app khởi động (self-heal path).
- Kiểm tra bị tắt trong Task Manager: `HKCU\...\Explorer\StartupApproved\Run\QuickStartLauncher` (REG_BINARY, byte đầu `0x03` = disabled) → hiện cảnh báo trên Settings: "Windows đang chặn mục khởi động này – bật lại trong Task Manager > Startup apps".
- `--autostart` flow:
  1. Đợi `delaySeconds` (mặc định 10s) — không hiện gì.
  2. `mode = showLauncher` → mở LauncherWindow bình thường.
  3. `mode = runProfiles` → hiện **AutostartToast** góc phải dưới: "Mở N ứng dụng (Profile A, B) sau 5s… [Mở ngay] [Hủy] [Mở Launcher]". Hết đếm → chạy lần lượt các profile trong `profileIds`, toast hiển thị tiến trình rút gọn, xong → thoát.
- Cảnh báo UI: action `runAsAdmin` trong autostart sẽ bật UAC prompt mỗi lần khởi động.

### 5.6 Khác

- **Single instance**: `Mutex("Global\\QuickStartLauncher.SingleInstance")`; instance thứ 2 gửi tín hiệu (named `EventWaitHandle`) để instance đầu `Activate()` cửa sổ rồi thoát.
- **CLI args**: `--autostart`, `--profile "<tên|id>"` (chạy ngay profile đó, tiện gắn shortcut desktop/hotkey), `--config` (mở thẳng Config).
- **Logging**: Serilog File sink, `rollingInterval: Day`, `retainedFileCountLimit: 14`; log INFO start/stop/run summary, WARN skip/UAC denied, ERROR kèm exception.
- **Global exception handler**: `DispatcherUnhandledException` + `TaskScheduler.UnobservedTaskException` → log + dialog lỗi thân thiện.

---

## 6. UI — Glassmorphism

### 6.1 Design tokens (`Themes/Glass.Colors.xaml`)

| Token | Dark | Light |
|---|---|---|
| `Bg.Gradient` (lớp dưới, nếu backdrop không có) | `#1B1F3A → #2A1747 → #0F2A3D` (3 điểm, góc 135°) | `#E8ECFF → #F6E9FF → #E3F6FF` |
| `Glass.Fill` | `#FFFFFF` @ 8% | `#FFFFFF` @ 55% |
| `Glass.FillHover` | `#FFFFFF` @ 12% | `#FFFFFF` @ 70% |
| `Glass.Stroke` (viền trên sáng, dưới tối – LinearGradient) | `#FFFFFF` 28% → 6% | `#FFFFFF` 90% → 40% |
| `Text.Primary` / `Text.Secondary` | `#F5F7FF` / `#F5F7FF` @ 65% | `#121426` / `#121426` @ 60% |
| `Accent` / `Accent.Gradient` | `#7C8CFF` → `#B57CFF` | `#5B6CFF` → `#9B5CFF` |
| `State.Success/Error/Warning` | `#4ADE80` / `#F87171` / `#FBBF24` | tương ứng đậm hơn |

- Corner radius: card 20, control 12, chip 999. Spacing scale 4/8/12/16/24.
- Font: `Segoe UI Variable` (fallback `Segoe UI`), icon: `Segoe Fluent Icons` (fallback `Segoe MDL2 Assets`) qua `SymbolIcon` của WPF-UI.
- Shadow: `DropShadowEffect` BlurRadius 30, Opacity 0.25, Depth 8 – **chỉ dùng cho cửa sổ/card lớn** (DropShadowEffect tốn GPU, không dùng trong list item).
- Animation: fade+scale-in 160ms khi mở cửa sổ; hover card nhấc 2px + sáng fill; trạng thái action chuyển màu 200ms; check ✓ vẽ bằng path animation.
- Theme `system`: đọc `HKCU\...\Themes\Personalize\AppsUseLightTheme`, theo dõi thay đổi qua WPF-UI `SystemThemeWatcher`.

### 6.2 Cửa sổ & backdrop

- Mọi cửa sổ là `ui:FluentWindow` với `WindowBackdropType="Acrylic"` (Launcher, Toast) / `"Mica"` (Config – đỡ nặng), `ExtendsContentIntoTitleBar="True"`, `WindowCornerPreference="Round"`.
- Win11 22H2+ (build ≥ 22621): backdrop thật qua `DWMWA_SYSTEMBACKDROP_TYPE (38)`; dark title qua `DWMWA_USE_IMMERSIVE_DARK_MODE (20)`.
- **Fallback Win10 / máy tắt transparency**: nền `Bg.Gradient` + `Glass.Fill` tăng opacity (dark 85%) → vẫn đẹp, không trong suốt. Kiểm tra `HKCU\...\Themes\Personalize\EnableTransparency`.
- Lớp "kính" bên trong: các `Border` style `GlassCard` (Fill + Stroke gradient + radius), đặt trên nền backdrop → cảm giác frosted glass.

### 6.3 LauncherWindow (≈ 420×auto, tối đa 560 cao)

```
╭──────────────────────────────────────────╮
│  ⚡ QuickStart                    ⚙   ✕  │  ← title bar tự vẽ, kéo được
│                                          │
│  [💼 Làm việc] [🏠 Nhà] [🎮 Giải trí]     │  ← chip profile (cuộn ngang), phím 1..9
│                                          │
│  ╭────────────────────────────────────╮  │
│  │ 🟢 Chrome · facebook.com      ✓    │  │  ← list icon + tên + trạng thái
│  │ 🟦 Zalo                       ⏳   │  │
│  │ 🟪 Microsoft Teams            ·    │  │
│  │ 🟦 Outlook                    ·    │  │
│  ╰────────────────────────────────────╯  │
│                                          │
│  ╭────────────────────────────────────╮  │
│  │            ▶  Begin                │  │  ← primary gradient button, Enter
│  ╰────────────────────────────────────╯  │
│  ▓▓▓▓▓▓▓░░░░░  2/4 · 0 lỗi              │  ← progress (hiện khi chạy)
╰──────────────────────────────────────────╯
```

- Topmost, mở giữa màn hình, nhớ vị trí lần cuối (tùy chọn).
- Profile rỗng → empty state: "Chưa có ứng dụng nào — [＋ Thêm ứng dụng]" mở thẳng App Picker.
- Khi chạy: Begin → "Đang chạy… (Hủy)"; item hiện spinner/✓/✕; ✕ hover hiện tooltip lỗi.
- Xong: 0 lỗi → hiện "Hoàn tất ✓" `closeDelayMs` rồi thoát; có lỗi & `keepOpenOnError` → giữ cửa sổ, nút "Thử lại mục lỗi" + "Đóng".
- Phím: `Enter` Begin, `Esc` đóng/hủy, `1..9` chọn profile, `Ctrl+,` mở Config.

### 6.4 ConfigWindow (≈ 1000×640, resize được, min 820×520)

```
╭────────────────┬──────────────────────────────────────┬──────────────────────╮
│ PROFILES       │  💼 Làm việc            [▶ Chạy thử] │  CHỈNH SỬA            │
│ ● 💼 Làm việc  │  ┌──────────────────────────────────┐│  [icon] Google Chrome │
│ ○ 🏠 Nhà       │  │⠿ [ic] Chrome · facebook.com  [●]⋯││  Ứng dụng: Chrome  ⟳ │
│ ○ 🎮 Giải trí  │  │⠿ [ic] Zalo                   [●]⋯││  ── Trình duyệt ──   │
│                │  │⠿ [ic] Teams                  [●]⋯││  Profile: [Work ▾]    │
│ [＋ Profile]   │  │⠿ [ic] Outlook                [○]⋯││  URL:                 │
│                │  └──────────────────────────────────┘│   facebook.com    ✕   │
│ ─────────────  │  [＋ Thêm ứng dụng] [＋ Website]     │   mail.google.com ✕   │
│ ⚙ Cài đặt      │  [＋ Lệnh] [＋ Chờ]                  │   [＋ Thêm URL]       │
│                │                                      │  ☑ Mở cửa sổ mới      │
│                │                                      │  ▸ Nâng cao           │
│                │                                      │  ☐ Bỏ qua khi tự KĐ   │
╰────────────────┴──────────────────────────────────────┴──────────────────────╯
                                         Đã lưu tự động ✓   (hoặc [Lưu] Ctrl+S)
```

- **Sidebar**: chọn profile để sửa; context menu: Đổi tên (inline), Nhân bản, Đặt làm mặc định, Xuất JSON, Xóa (confirm, không xóa profile cuối). Nút "Nhập profile…" ở cuối.
- **Danh sách action** (cột giữa): card có handle ⠿ kéo thả (gong-wpf-dragdrop, có adorner preview), icon, tên + mô tả 1 dòng, toggle `enabled`, menu ⋯ (Sửa / Nhân bản / Chạy thử / Xóa). Delay hiển thị dạng card mảnh "⏱ Chờ 1.5s". Multi-select + Delete.
- **Editor** (cột phải): template theo `kind`:
  - *App*: header icon+tên, nút "Đổi ứng dụng" (mở picker), nếu là browser → section Trình duyệt (dropdown profile đọc từ §5.3 hiển thị "Tên (email)", list URL có thêm/xóa/kéo thứ tự, checkbox cửa sổ mới). "Nâng cao": arguments, working dir, run as admin (kèm cảnh báo UAC), delay sau khi mở.
  - *Website*: URL (validate realtime), chọn trình duyệt (Mặc định + list trình duyệt cài sẵn có icon), cửa sổ mới.
  - *Command*: shell (cmd/PowerShell), ô lệnh monospace nhiều dòng, working dir (browse folder), ẩn cửa sổ, run as admin.
  - *Delay*: slider + numeric (0–60000 ms).
  - Chung: "Bỏ qua khi khởi động cùng Windows", "Chạy thử" (gọi `RunSingleAsync`, toast kết quả).
- **Lưu**: auto-save có debounce 500ms + chỉ báo "Đã lưu ✓" (đơn giản hơn Save/Cancel, ít mất dữ liệu). `Ctrl+Z` hoàn tác thao tác xóa gần nhất (snackbar "Đã xóa Zalo · Hoàn tác").
- Responsive: cửa sổ < 1000px → editor thành drawer trượt từ phải.

### 6.5 AppPickerDialog (modal, ≈ 760×560)

```
╭──────────────────────────────────────────────────────╮
│ Thêm ứng dụng                                    ✕   │
│ 🔍 [ Tìm ứng dụng…                             ]     │  ← focus sẵn, debounce 120ms
│ [Tất cả] [Trình duyệt] [Store] [Đã thêm]            │  ← filter chip
│ ┌────┐ ┌────┐ ┌────┐ ┌────┐ ┌────┐ ┌────┐            │
│ │ ic │ │ ic │ │ ic✓│ │ ic │ │ ic │ │ ic │            │  ← lưới icon 64px, tên 2 dòng
│ │Chro│ │Zalo│ │Team│ │Outl│ │VSCo│ │Cốc │            │     tick khi chọn (multi-select)
│ └────┘ └────┘ └────┘ └────┘ └────┘ └────┘            │
│ …                                                    │
│ [📁 Chọn file .exe…]  [⌨ Lệnh tùy chỉnh]   Đã chọn 2 │
│                               [Hủy]  [Thêm 2 ứng dụng]│
╰──────────────────────────────────────────────────────╯
```

- `VirtualizingWrapPanel` (WPF-UI có `VirtualizingItemsControl`/`VirtualizingWrapPanel`) — load icon lazy khi item vào viewport.
- Phím: gõ là tìm; `↑↓←→` di chuyển; `Space` tick; `Enter` thêm (nếu chưa tick gì thì thêm item đang focus); double-click = thêm ngay 1 app.
- Badge "Trình duyệt" / "Store" trên tile; app đã có trong profile hiện mờ + nhãn "Đã thêm".
- Thêm browser → tạo AppAction với `browser` rỗng và mở editor ngay để user nhập URL.
- Chế độ "Đổi ứng dụng" (từ editor) = single-select.
- Đang refresh nền → spinner nhỏ cạnh ô search, list tự cập nhật.

### 6.6 SettingsPage

- **Khởi động cùng Windows** — segmented control 3 lựa chọn: `Không` | `Tự chạy` | `Chỉ mở Launcher`.
  - `Tự chạy` → checklist các profile (kéo thứ tự), "Trễ sau khi đăng nhập" (0–120s), "Đếm ngược cho phép hủy" (0–15s).
  - Hiển thị dòng xem trước: "Sau khi đăng nhập 10s sẽ mở 7 ứng dụng từ Làm việc, Nhà (2 mục bị bỏ qua)."
  - Banner cảnh báo nếu StartupApproved đang disabled (§5.5) hoặc có action runAsAdmin.
- **Sau khi chạy xong**: tự đóng / giữ cửa sổ; giữ cửa sổ khi có lỗi.
- **Giao diện**: Hệ thống / Sáng / Tối.
- **Dữ liệu**: Mở thư mục config, Mở thư mục log, Làm mới danh sách ứng dụng, Xuất/Nhập toàn bộ config.
- **Giới thiệu**: phiên bản.

---

## 7. Lộ trình triển khai (checklist)

### Phase 0 — Chuẩn bị
- [x] `git init`, thêm `.gitignore` (bin/obj/.vs), commit bản v2 nguyên trạng (`chore: snapshot v2 before remake`).
- [x] Tạo nhánh `remake-v3`.
- [x] Backup thủ công `%LocalAppData%\QuickStart\config\config.json`.

### Phase 1 — Nền móng
- [x] Xóa code v2 (Views, ViewModels, Models, Services, Core); giữ `Assets/`.
- [x] Cập nhật `csproj` + packages (§2), `NativeMethods.txt`.
- [x] `App.xaml.cs`: DI container, Serilog, single-instance, parse CLI args, global exception handler.
- [x] Models v3 + JSON options (§4).
- [x] `ConfigService` (load/save atomic) + `ConfigMigrator` + unit test với config thật.
- **Done khi**: app chạy lên cửa sổ trống, migrate config cũ ra đúng JSON v3.

### Phase 2 — Services lõi
- [x] `ShellAppsFolderProvider` + `IconCache` (§5.1).
- [x] `TextNormalizer` + `FuzzySearch` + unit test acceptance (§5.2).
- [x] `BrowserCatalog` + `BrowserProfileReader` + default browser (§5.3).
- [x] `LaunchService` với 4 handler + AUMID activation (§5.4).
- [x] `StartupService` (§5.5).
- **Done khi**: console/test harness liệt kê được app + icon, chạy được 4 action của config thật.

### Phase 3 — Design system
- [x] `Glass.Colors.xaml` (light/dark), `Glass.Controls.xaml`, animations (§6.1).
- [x] Backdrop + fallback Win10/transparency off (§6.2).
- [x] Trang demo nội bộ (debug only) hiển thị mọi control để soát style.

### Phase 4 — Launcher + Autostart toast
- [x] `LauncherWindow` + `LauncherViewModel` (§6.3), progress realtime, cancel, retry lỗi.
- [x] `AutostartToast` + flow `--autostart` (§5.5).
- [x] `--profile` CLI.

### Phase 5 — Config + Picker + Settings
- [x] `ConfigWindow` shell + sidebar profile (CRUD, duplicate, import/export).
- [x] Action list: drag reorder, toggle, menu ⋯, multi-select, undo xóa.
- [x] Editors 4 loại + Chạy thử.
- [x] `AppPickerDialog` (§6.5).
- [x] `SettingsPage` (§6.6) nối với `StartupService`.
- [x] Auto-save debounce.

### Phase 6 — Hoàn thiện & phát hành
- [x] Rà accessibility: tab order, `AutomationProperties.Name`, contrast text trên glass ≥ 4.5:1.
- [x] Hiệu năng: đo thời gian mở Launcher (mục tiêu < 400ms cold start với ReadyToRun), picker, enumerate.
- [x] `dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=true`.
- [x] Cập nhật `README.md`, viết `spec.md` v3 (thay spec v2), xóa `config.json` mẫu cũ → mẫu v3.
- [ ] Chạy toàn bộ test acceptance §8 trên máy thật, khởi động lại Windows để test autostart. **Còn kiểm chứng vật lý; xem VERIFICATION.md.**

---

## 8. Acceptance criteria

**Chọn app**
- [ ] Picker liệt kê đủ app như Start Menu, gồm app Store (Teams mới, Calculator, Outlook mới nếu có) kèm icon đúng.
- [x] Gõ `chrmoe` → Google Chrome trong top 3; `outlok` → Outlook; `zalo` → Zalo; tìm có/không dấu đều ra.
- [x] Chọn 3 app cùng lúc → thêm 3 action đúng thứ tự chọn.
- [ ] Browse `.exe` bất kỳ → action hợp lệ, có icon.
- [x] Mở picker lần 2 ≤ 200ms (có cache).

**Trình duyệt**
- [ ] Chọn Chrome/Edge/Cốc Cốc → editor hiện section Trình duyệt, dropdown profile hiển thị tên thật.
- [ ] 2 URL + cửa sổ mới → mở 1 cửa sổ mới, 2 tab, đúng profile.
- [ ] Website action `default` → mở bằng trình duyệt mặc định Windows.

**Thực thi**
- [x] Đúng thứ tự; `delayAfterMs`/Delay chờ đúng (±150ms).
- [x] 1 action lỗi (path sai) → đánh ✕, các action sau vẫn chạy, log ERROR.
- [ ] UAC bấm No → `Skipped`, không crash.
- [x] Action `enabled=false` không chạy.
- [x] Xong 0 lỗi → process thoát ≤ 2s.
- [ ] Command `start msteams:` mở Teams, không hiện cửa sổ cmd khi `hidden`.

**Khởi động cùng Windows**
- [ ] Mode `Không` → không có value trong `HKCU\...\Run`.
- [ ] Mode `Tự chạy` (profile A, B) → sau logon + delay hiện toast đếm ngược; Hủy → không mở gì; hết đếm → chạy A rồi B, bỏ qua action `skipOnAutostart`.
- [ ] Mode `Chỉ mở Launcher` → sau logon hiện Launcher.
- [ ] Di chuyển exe sang chỗ khác rồi mở 1 lần → value Run tự cập nhật path.
- [ ] Tắt app trong Task Manager > Startup → Settings hiện cảnh báo.

**Profiles & config**
- [ ] Tạo/đổi tên/nhân bản/xóa profile; không xóa được profile cuối.
- [x] Đổi tên profile không làm mất lựa chọn trong startup.
- [x] Config v2 thật migrate đủ 4 action, có file `.v2.bak.json`.
- [ ] Export → Import profile trên máy khác (path không tồn tại → action hiện cảnh báo "Không tìm thấy ứng dụng", không crash).
- [ ] Tắt máy đột ngột khi đang lưu không làm hỏng config (atomic write).

**UI**
- [ ] Win11: nền Acrylic/Mica thật; Win10 hoặc tắt transparency: fallback gradient vẫn đọc rõ.
- [ ] Light/Dark/System chuyển realtime.
- [ ] Mọi thao tác chính làm được bằng bàn phím.

---

## 9. Rủi ro & lưu ý

| Rủi ro | Cách xử lý |
|---|---|
| COM shell interop phức tạp (CsWin32 thiếu định nghĩa COM) | Fallback `Microsoft-WindowsAPICodePack-Shell`; enumerate trên STA thread riêng |
| Một số item AppsFolder không resolve được exe (shortcut đặc biệt, PWA) | Lưu AUMID, chạy qua `shell:AppsFolder\<aumid>`; không hỗ trợ args → disable ô args + ghi chú |
| PWA của Chrome/Edge (AUMID dạng `Chrome._crx_…`) | Vẫn chạy được qua AUMID; nhận diện không phải browser |
| `DropShadowEffect`/blur làm giật khi list dài | Chỉ dùng shadow ở card lớn; list item dùng border + fill phẳng; virtualization |
| Backdrop WPF-UI lỗi với một số driver/RDP | Tự động fallback gradient khi `DwmSetWindowAttribute` fail hoặc chạy trong RDP (`SystemParameters.IsRemoteSession`) |
| Autostart chạy quá sớm khi mạng/Explorer chưa sẵn | `delaySeconds` mặc định 10s + toast đếm ngược |
| runAsAdmin trong autostart gây phiền UAC | Cảnh báo trong Settings và editor |
| Trình duyệt đang chạy giữ khóa `Local State` | Đọc với `FileShare.ReadWrite`, try/catch → dropdown chỉ có "Mặc định" |

---

## 10. Ngoài phạm vi v3 (để sau)

- Global hotkey chạy profile; tray icon.
- Nhóm action chạy song song.
- Sắp xếp cửa sổ (đặt app vào màn hình/vị trí cụ thể sau khi mở).
- Chờ app "sẵn sàng" (phát hiện cửa sổ chính) trước khi chạy action tiếp.
- Profile theo lịch (ngày làm việc vs cuối tuần) / theo mạng Wi-Fi.
- Auto-update, MSIX.


## 11. Kết quả triển khai — 21/10/2026

Mã v3 đã triển khai, Debug build sạch, 73/73 harness checks, 6/6 Release smoke checks. Bản chạy: artifacts/release/QuickStartLauncher.exe.

Báo cáo và giới hạn: **VERIFICATION.md**. Cold start thực đo 1247 ms, **chưa đạt** mục tiêu 400 ms. Không đánh dấu hoàn tất kiểm tra reboot/UAC thật/Win10/desktop input khi chưa có bằng chứng. Checkbox nghiệm thu chưa đánh dấu không đồng nghĩa chưa có code; một số cần xác nhận bằng thao tác vật lý.

Snapshot v2: c9aba78. Branch: remake-v3. Config thật đã migrate đủ bốn mục và có backup.
