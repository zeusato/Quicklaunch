# Quick Start Launcher — SPEC (v2)

> App Windows siêu gọn: bấm **Begin** để khởi chạy tuần tự danh sách hành động (mở app / mở website / chạy lệnh / chờ), xong là **thoát**. Có **Config** để tạo/sửa danh sách. Không chạy nền.

---

## 0) Hướng dẫn cho MCP Kilo (Scaffold & Deliverables)

- **Stack**: .NET 8 (C#) + **WPF** + MVVM tối giản.
- **Project name**: `QuickStartLauncher` (Exe).
- **Output**: 1 file exe + thư mục `config/`, `logs/` bên cạnh (dev build); option MSIX sau.
- **Packages** (không bắt buộc nhưng gợi ý):
  - Logging: `Serilog`, `Serilog.Sinks.File`.
  - JSON: `System.Text.Json` (built‑in). Không cần Newtonsoft nếu không cần.
- **Deliverables**:
  1. Source code build được `Debug/Release`.
  2. `README.md` (cách build/chạy).
  3. `spec.md` (file này).
  4. `QuickStartLauncher.exe` (Release) + sample `config.json`.

---

## 1) Mục tiêu & Phạm vi

- Popup nhỏ có **Begin** và **⚙ Config**.
- Action hỗ trợ: `app`, `website`, `command`, `delay`.
- **Tìm ứng dụng theo tên** (fuzzy) từ Start Menu + Registry. **Fallback**: duyệt `.exe`.
- **Nhận diện trình duyệt** → hiện **Default URL** khi action là app-browse.
- **Profiles** nhiều cấu hình (Default bắt buộc).
- Cấu hình lưu **JSON**; log nhẹ ra file.
- Hoàn tất queue → **thoát app**.

**Ngoài phạm vi v1**: auto‑run at startup, auto‑update, UWP AppUserModelID, sync cloud.

---

## 2) UI/UX

### 2.1 Popup chính

- Kích thước \~320–360px rộng; AlwaysOnTop; có thể kéo.
- Thành phần: Nút **Begin** (primary), icon **⚙** (góc phải trên), thanh trạng thái nhỏ.
- Hành vi: nhấn **Begin** → chạy queue theo **active profile** → hiển thị tiến trình + đếm lỗi → **thoát** sau khi xong.

### 2.2 Màn hình Config

- Sections: `Actions` | `Profiles` | `Search Index`.
- **Actions**:
  - Danh sách có **drag‑and‑drop reorder**, Duplicate, Delete.
  - `Add Action` → chọn loại: `App` / `Website` / `Command` / `Delay`.
  - `Test Selected` để chạy thử **1 action** đang chọn.
- **Profiles**: CRUD profile, set **Active**.
- **Search Index**: hiện `items`, `last updated`; nút **Rebuild Index** (progress modal).
- Keyboard: `Enter` (Begin), `Esc` (Close), `Ctrl+S` (Save trong Config).

---

## 3) Action Types & Fields

### 3.1 `app`

- Chọn ứng dụng bằng **Search fuzzy** (ưu tiên Start Menu) hoặc **Browse .exe**.
- Trường:
  - `name` (string, hiển thị)
  - `path` (string, bắt buộc nếu chọn thủ công)
  - `arguments` (string, optional)
  - `runAsAdmin` (bool)
  - `isBrowser` (bool, auto‑detect)
  - `url` (string, optional – hiện nếu `isBrowser`)
  - `newWindow` (bool, optional – cho browser)

### 3.2 `website`

- Trường: `url` (string, bắt buộc), `browser` (`default|chrome|msedge|firefox|brave|opera`), `newWindow` (bool optional).

### 3.3 `command`

- Trường: `command` (string), `workingDir` (string optional), `runAsAdmin` (bool optional).

### 3.4 `delay`

- Trường: `ms` (int ≥ 0).

**JSON mẫu**

```json
{
  "activeProfile": "Default",
  "profiles": [
    {
      "name": "Default",
      "actions": [
        { "type": "app", "name": "Notepad++", "path": "C:\Program Files\Notepad++\notepad++.exe" },
        { "type": "app", "name": "Chrome", "path": "C:\Program Files\Google\Chrome\Application\chrome.exe", "isBrowser": true, "url": "https://facebook.com", "newWindow": true },
        { "type": "delay", "ms": 1500 },
        { "type": "command", "command": "cmd /c start ms-settings:windowsupdate" },
        { "type": "website", "url": "https://portal.company", "browser": "edge" }
      ]
    }
  ]
}
```

---

## 4) Tìm ứng dụng như Windows Search

### 4.1 Nguồn index

1.  **Start Menu (.lnk)**
    - All users: `C:\ProgramData\Microsoft\Windows\Start Menu\Programs`
    - User: `%AppData%\Microsoft\Windows\Start Menu\Programs`
    - Resolve `.lnk` → target `.exe`, tên hiển thị, icon.
2.  **Registry Uninstall** (DisplayName)
    - `HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall`
    - `HKLM\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall`
    - `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall`
3.  **Program Files scan** (tùy chọn) — chỉ các thư mục whitelist thông dụng.

### 4.2 Fuzzy match & ưu tiên

- Normalize: lowercase, bỏ dấu, bỏ từ rác (`setup`, `updater`, `helper`).
- Alias: tên shortcut, file exe, `ProductName`, `DisplayName`.
- Scoring: `exact > startswith > substring > fuzzy(Levenshtein/token)`.
- Ưu tiên kết quả từ **Start Menu**.

### 4.3 Cache & Rebuild

- Lưu `%LocalAppData%\QuickStart\appIndex.json`.
- Build lần đầu khi mở Config; có **Rebuild Index**.

---

## 5) Nhận diện trình duyệt & mở URL

- **Whitelist exe**: `chrome.exe`, `msedge.exe`, `firefox.exe`, `brave.exe`, `opera.exe`, `iexplore.exe`.
- Double‑check metadata `ProductName`/`CompanyName`.
- Khi `isBrowser==true`: hiển thị field **Default URL** + `newWindow`.

**Lệnh mở URL tham khảo**

- Chrome: `chrome.exe --new-window "{url}"` (hoặc `--new-tab`)
- Edge: `msedge.exe --new-window "{url}"`
- Firefox: `firefox.exe -new-window "{url}"`
- Brave: `brave.exe --new-window "{url}"`
- Opera: `opera.exe --new-window "{url}"`
- **Default browser**: `UseShellExecute=true` với `{url}`.

---

## 6) Thực thi (Begin)

- Chạy **tuần tự** qua danh sách action.
- `app`: nếu browser + có `url` → chèn args theo bảng trên; hỗ trợ `arguments`, `runAsAdmin` (`Verb=runas`).
- `website`: `default` → shell execute; chỉ định browser → gọi exe tương ứng + args.
- `command`: gọi trực tiếp hoặc qua `cmd /c`; tôn trọng `workingDir`.
- `delay`: `Task.Delay(ms)`.
- Không đặt timeout kill process (fire‑and‑forget). Xong hàng đợi → **thoát app**.

---

## 7) Logging & Lỗi

- File log: `%LocalAppData%\QuickStart\logs\yyyy-MM-dd.log`.
- Mức `INFO` (start/stop), `ERROR` (chi tiết action lỗi + stacktrace).
- UI hiển thị tổng số action và số lỗi. Không chặn queue khi lỗi.

---

## 8) Lưu trữ & Cấu trúc thư mục

- Thư mục: `%LocalAppData%\QuickStart\config` (JSON), `%LocalAppData%\QuickStart\logs` (log), `%LocalAppData%\QuickStart\appIndex.json` (cache).
- Output build gợi ý:

```
/QuickStartLauncher
  /config
  /logs
  QuickStartLauncher.exe
```

---

## 9) Kiến trúc code (MVVM gọn)

- `Views/` (Popup, ConfigWindow)
- `ViewModels/` (MainVM, ConfigVM, ActionsVM, ProfilesVM)
- `Models/` (Action models, Profile)
- `Services/`
  - `IActionExecutor` (App/Website/Command/Delay)
  - `IAppIndexService` (scan + fuzzy search)
  - `IConfigService` (load/save JSON)
  - `ILogService` (wrap Serilog)
- `Core/` (Utils: Normalize, Fuzzy, FileOps)

---

## 10) Acceptance Criteria (Test)

- **Search fuzzy**: gõ `chrmoe` → trả `Google Chrome` trong top 3.
- **Browse fallback**: chọn `.exe` bất kỳ → tạo action hợp lệ.
- **Browser detect**: chọn `chrome.exe` → hiện `Default URL`; chạy mở đúng URL.
- **Website default**: mở bằng browser mặc định của Windows.
- **Command**: `cmd /c echo hello` chạy không treo UI.
- **Delay**: 1500ms tạo trễ \~1.5s giữa 2 action.
- **Order**: thực thi đúng thứ tự; lỗi 1 action không dừng queue.
- **Exit**: hoàn tất queue → process thoát trong ≤2s.
- **Log**: tạo file theo ngày; có entry ERROR khi lỗi.

---

## 11) Hiệu năng & Ổn định

- Build index lần đầu ≤ 3s với \~200 shortcut.
- Tìm kiếm trả kết quả ≤ 100ms sau khi dừng gõ (debounce 150ms).
- Begin chạy async, UI không “đơ”.

---

## 12) Bảo mật & Quyền

- `runAsAdmin` chỉ khi user bật; để Windows UAC xử lý.
- Không lưu secrets; không chỉnh registry hệ thống.

---

## 13) Đóng gói & Phát hành

- Icon `.ico` 256px.
- Tên exe: `QuickStartLauncher.exe`.
- (Tùy chọn) profile portable: nếu không có quyền ghi `%LocalAppData%`, lưu cạnh exe.
- (Tùy chọn) MSIX + App Installer (để sau v1).

---

## 14) Hướng mở rộng

- UWP/Store Apps (`shell:AppsFolder` + AppUserModelID).
- **Parallel groups** (nhóm action chạy song song).
- Export/Import profile.
- Global Hotkey để chạy nhanh profile.
- Auto‑run at startup; Auto‑update.

---

## Phụ lục — Wireframe ASCII

```
+----------------------------------+
| Quick Start Launcher        [⚙]  |
|----------------------------------|
|                                  |
|             Begin                |
|                                  |
|  Status: Ready                   |
+----------------------------------+

[Config]
+---------------------------------------------------+
| Profile: [Default  v]        [Save] [Cancel]      |
|---------------------------------------------------|
| Actions                                           |
| 1) App   | Chrome (isBrowser, url=https://...)    |
| 2) Delay | 1500 ms                                |
| 3) App   | Notepad++                              |
|  [Add Action v]  [Test Selected]                  |
|---------------------------------------------------|
| Search Index:  items=231  last=2025-08-11         |
| [Rebuild Index]                                   |
+---------------------------------------------------+