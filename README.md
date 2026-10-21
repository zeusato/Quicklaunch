# QuickStartLauncher 3

Launcher Windows viết bằng .NET 8 / WPF. Mở ứng dụng, website và lệnh theo profile; chọn ứng dụng từ Windows, gồm cả Store.

## Sử dụng

Bản đã build: **artifacts/release/QuickStartLauncher.exe**. Cần Windows 10 2004+, x64 và .NET Desktop Runtime 8.

- Chọn profile rồi nhấn **Begin** hoặc Enter.
- **⚙ Cấu hình**: thêm ứng dụng, website, lệnh hoặc thời gian chờ.
- Picker: tìm gần đúng/không dấu, tick nhiều ứng dụng theo thứ tự muốn thêm, hoặc Browse exe.
- Browser editor: URL/tab, profile và cửa sổ mới.
- Kéo tác vụ, hoặc ↑ ↓ để sắp xếp; nhân bản, chạy thử, xóa và hoàn tác.
- Autosave sau 500 ms, Ctrl+S lưu ngay; lỗi lưu hiện ở thanh trạng thái.
- Settings: Không / Tự chạy / Chỉ mở Launcher; profile tự chạy có thứ tự.
- Toast có Mở ngay, Hủy, Mở Launcher; giữ lỗi để thử lại.

Phím Launcher: Enter chạy/hủy, Esc hủy/đóng, 1–9 chọn profile, Ctrl+, mở cấu hình. Picker: mũi tên, Space tick, Enter thêm, Esc hủy. Danh sách tác vụ: Delete, Alt+↑/↓; Ctrl+Z khi focus ngoài ô văn bản hoàn tác xóa tác vụ.

## CLI

~~~powershell
.\QuickStartLauncher.exe
.\QuickStartLauncher.exe --config
.\QuickStartLauncher.exe --profile "Default"
.\QuickStartLauncher.exe --profile "<guid>"
.\QuickStartLauncher.exe --autostart
~~~

Debug có **--design** mở gallery control. Instance thứ hai kích hoạt cửa sổ đang chạy.

## Dữ liệu

Mặc định: **%LocalAppData%\QuickStart**.

| Mục | Vị trí |
|---|---|
| Config v3 | config/config.json |
| Backup v2 | config/config.v2.bak.json |
| Backup trước nhập | config/config.before-import.*.json |
| Cache ứng dụng / icon | cache/apps.json / cache/icons/ |
| Log xoay vòng ngày, giữ 14 file | logs/ |

Có **portable.flag** cạnh exe thì dữ liệu nằm cạnh exe; thư mục phải ghi được.

Migration backup trước khi thay file. File hỏng hoặc phiên bản không hỗ trợ được giữ nguyên, hiện cảnh báo và khóa ghi tới khi nhập v3 hợp lệ. Save dùng temp + flush + atomic replace.

Nhập/nhân bản profile tạo ID mới; đổi tên giữ ID nên không mất chọn startup. Nhập toàn bộ config có xác nhận và backup. Lệnh chạy như đã nhập; chỉ nhập config từ nguồn đã biết.

## Build / test

~~~powershell
dotnet build QuickStartLauncher.csproj -c Debug
dotnet run --project Tests/QuickStartLauncher.Tests.csproj -c Debug
dotnet publish QuickStartLauncher.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:PublishReadyToRun=true -o artifacts/release
powershell -NoProfile -File scripts/Verify-Release.ps1
~~~

Harness WPF trả exit code khác 0 nếu lỗi. Test gồm fixture v2 thật, fake process/UAC, registry trong khóa riêng, AppsFolder thật và ảnh render UI. Bằng chứng ở **artifacts/tests/**. Không mở ứng dụng fixture trừ khi truyền **--launch-real**.

Smoke Release dùng fixture portable chỉ chạy Delay, giữ/khôi phục Run value. Đóng QuickStart trước khi chạy script.

Xem **VERIFICATION.md** cho kết quả và phần cần kiểm tra trực tiếp; **spec.md** cho schema và kiến trúc.

## Package đã pin

WPF-UI 4.3.0; CommunityToolkit.Mvvm 8.4.2; DI 8.0.1; Serilog 4.4.0 / File 7.0.0; gong-wpf-dragdrop 4.0.0; CsWin32 0.3.366; VirtualizingWrapPanel 2.5.4.

COM shell dùng ABI Windows trên STA riêng; CsWin32 sinh P/Invoke GDI. Không gọi PowerShell để enumerate app. Ứng dụng không có telemetry hay auto-update.
