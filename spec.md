# QuickStartLauncher v3 — specification

## Kiến trúc

.NET 8, Windows 10 build 19041+, WPF-UI FluentWindow. CommunityToolkit.Mvvm, POCO model độc lập UI, DI trong App, Serilog file log 14 ngày.

- Config: camelCase JSON, enum string, migrate v2, validate, backup, atomic save.
- Apps: IShellItem2/AppsFolder trên STA; dedupe, lọc rác, sort vi-VN; icon IShellItemImageFactory được Freeze, giải phóng COM/HBITMAP, cache RAM/PNG.
- Search: tiếng Việt, alias/acronym, Damerau-Levenshtein; debounce 120 ms.
- Browsers: Chrome/Edge/Brave/Cốc Cốc/Opera/Firefox, App Paths registry; Chromium Local State với FileShare.ReadWrite; Firefox profiles.ini.
- Launch: snapshot queue, tuần tự, cancel, progress, bốn loại action; Process.Start trên STA nền.
- Startup: HKCU Run và StartupApproved, self-heal đường dẫn khi mở app/lưu.
- Theme: Acrylic/Mica + DWM corner/dark mode; light/dark/system, fallback khi tắt transparency/RDP/Windows cũ.
- Views: Launcher, Config (Actions/Settings/editor drawer), picker ảo hóa, toast, gallery Debug.
- ViewModels: observable wrappers, profile CRUD/import/export, action reorder/undo, autosave 500 ms.

## Schema

Xem config.json ở root cho mẫu hợp lệ.

AppConfig: version=3, settings, activeProfileId, profiles.
Profile: GUID id, name, icon, actions.
LaunchAction chung: kind, GUID id, name, enabled=true, skipOnAutostart=false, delayAfterMs=0.
- app: target {aumid?, path?}, arguments, workingDir?, runAsAdmin, browser? {urls[], newWindow, profileDir?}.
- website: url, browserId=default, newWindow.
- command: command, shell=cmd|powershell|pwsh, workingDir?, hidden=true, runAsAdmin.
- delay: ms=0..60000.

Settings: theme=system|light|dark; startup {mode=off|runProfiles|showLauncher, profileIds[], delaySeconds=0..120, countdownSeconds=0..15}; closeWhenDone=true, closeDelayMs=1200, keepOpenOnError=true.

Discriminator duy nhất kind. Rename giữ ID; import/duplicate profile tạo ID mới cho profile/action.

## Migration / persistence

Config chưa có version: map $type app/command/website/delay sang v3, shell 0/1 thành cmd/powershell, giữ URL/profile/newWindow. Loại không nhận diện làm migration thất bại để không bỏ dữ liệu. Startup mặc định off.

Backup trước replace; nếu backup v2 đã tồn tại nhưng khác nội dung, tạo thêm bản timestamp. Sau render, khớp exe path với AppsFolder để enrich AUMID. File hỏng được giữ và khóa ghi, import v3 hợp lệ khôi phục.

Save snapshot JSON trước await, dùng semaphore, WriteThrough + Flush(true), File.Replace (Move khi lần đầu). Lỗi lưu được hiện ra UI.

## Thực thi

Pending → Running → Done/Failed/Skipped. Lỗi không dừng queue. Disabled/skipOnAutostart theo ngữ cảnh. Delay có countdown, delayAfter sau action; cancel không đóng ứng dụng đã mở.

App ưu tiên exe tồn tại, resolve path từ AUMID khi thiếu. Store dùng IApplicationActivationManager. Win32/PWA chỉ có AUMID dùng Explorer shell:AppsFolder; có tham số không truyền được thì báo lỗi. UAC 1223 là Skipped.

URL chỉ http/https/file và quote theo Windows. Chromium dùng profile-directory/new-window; Firefox -P/new-window/new-tab. Website default dùng shell URL handler, newWindow do browser mặc định quyết định.

Cmd /d /c. PowerShell/pwsh dùng EncodedCommand UTF-16LE giữ nháy/nhiều dòng. Fire-and-forget. Hidden: UseShellExecute=false/CreateNoWindow. Admin: UseShellExecute=true, không ẩn.

## Startup / vòng đời

Run value QuickStartLauncher = quoted exe + --autostart. Off xóa value; chế độ khác cập nhật path. StartupApproved byte 3/7 hiện cảnh báo.

Autostart: delay không cửa sổ → launcher hoặc toast countdown → profileIds theo thứ tự. Mở Launcher từ toast giữ trạng thái để thử lại. Xong đóng sau closeDelayMs; lỗi + keepOpenOnError thì giữ.

Global mutex + named event, instance thứ hai activate. --profile nhận tên/GUID chạy ngay; --config mở cấu hình; --design chỉ Debug.

## Validation

Tests/ là WPF test executable: fixture migration, fake process/UAC, registry test riêng, AppsFolder thật, VM và render UI. scripts/Verify-Release.ps1 test exe portable. VERIFICATION.md ghi kết quả và giới hạn thực tế.
