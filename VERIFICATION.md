# Verification — QuickStartLauncher v3

## v3.0.3 — ẩn thông báo, theo dõi nền (29/10/2026)

- Báo đã gửi N lệnh tại thời điểm gọi Windows mở ứng dụng, không chờ lời gọi trả về. Sau thời gian giữ kết quả, toast ẩn và tiến trình tiếp tục theo dõi nền; lỗi đến muộn hiện lại thông báo. Không thay đổi cơ chế mở đồng thời của v3.0.2.
- Dismiss/đóng cửa sổ/Mở Launcher không hủy công việc. Chỉ kết thúc tiến trình sau khi theo dõi xong và không còn cửa sổ làm việc; giữ lỗi theo cài đặt KeepOpenOnError.
- Harness: **99 passed, 0 failed**. Kiểm thử WPF thật giữ lời gọi mở chưa trả về, kiểm tra cửa sổ đã ẩn; success không hiện lại, late failure hiện lại, Alt+F4 đóng được, chờ tìm target không báo nhầm đã gửi. Hình kiểm tra: `artifacts/tests/autostart-dispatched.png`, `autostart-late-error.png`.
- Build Release self-contained thành công. Tự chạy **2/2 đạt**, evidence `artifacts/autostart-20261029-093445-176/`; smoke Release **6/6 đạt**, evidence `artifacts/smoke-20261029-093501/`.
- Báo cáo máy khách đã xác nhận v3.0.2 gửi năm lệnh trong 37 ms; thời gian trả về của SHA PC 108 giây và Outlook 27 giây. Chưa xác định nguyên nhân sâu hơn của độ trễ đó; v3.0.3 thay cách thông báo và theo dõi, không tuyên bố làm ứng dụng đích khởi động nhanh hơn.

## v3.0.2 — tự chạy tức thời, giao diện tối mặc định (28/10/2026)

- Tự chạy gửi các lệnh mở độc lập, đồng thời giữa các tác vụ và profile; không đợi toast render, đếm ngược, delay đăng nhập, DelayAction hoặc DelayAfter. Chạy thủ công vẫn giữ thứ tự và delay. Các mục disabled/skipOnAutostart vẫn bỏ qua.
- Không quét AppsFolder để mở exe trình duyệt đã xác định hoặc Store AUMID; website ưu tiên tìm trình duyệt trong App Paths. Không làm enrichment danh sách ứng dụng trong luồng tự chạy.
- Config mới mặc định Dark; theme đã lưu được giữ. Các trường delay cũ vẫn đọc được nhưng không áp dụng cho tự chạy.
- Harness: **90 passed, 0 failed**, gồm giữ một lệnh mở bị kẹt và xác nhận các lệnh cùng/khác profile vẫn được gửi, lỗi độc lập, hủy và không chạy trùng.
- Exe Release self-contained build thành công. Smoke tự chạy: **2/2 đạt** với cấu hình cũ delay=120s, countdown=15s và các delay tác vụ 60s. Hai yêu cầu mở thực được gửi cùng timestamp; khoảng **17–21 ms từ lúc bắt đầu profile**, **1.854–2.392 giây từ lúc tạo process** trên máy thử. Evidence: `artifacts/autostart-20261028-154502-267/results.json` và log cạnh file.
- Smoke Release thủ công: **6/6 đạt**, evidence `artifacts/smoke-20261028-154524/`. UI render phần tự chạy và kiểm tra binding đạt.
- Thời gian từ đăng nhập Windows đến lúc Windows tạo process, hoặc từ lệnh mở đến lúc ứng dụng đích hiện cửa sổ, không nằm trong phép đo gửi lệnh. Chưa có log hay kiểm thử reboot trên máy khách báo chậm 5–6 phút.

## Kiểm tra bổ sung 28/10/2026 — tự khởi động

- Tái hiện trên bản `artifacts/publish`: một lần `--autostart` chạy thành công; lần thứ hai trong thời gian chờ làm hủy delay, chỉ mở Launcher và không chạy profile. Evidence: `artifacts/autostart-20261028-140653-471/results.json`.
- Sửa `App.OnActivated` để bỏ qua yêu cầu `--autostart` trùng, giữ nguyên luồng đầu tiên; thêm log đường dẫn exe/config, chế độ startup và hủy delay.
- Build self-contained tại `artifacts/autostart-fix` thành công. Hai tình huống trên đều chạy đúng một profile, hoàn thành action Delay và thoát mã 0: `artifacts/autostart-20261028-140951-205/results.json`.
- Harness hiện tại: **79 passed, 0 failed**. Script chẩn đoán đã chạy thử trên máy phát triển.
- Ảnh báo lỗi thuộc máy khác; chưa có log/registry từ máy đó, nên chưa xác nhận lỗi yêu cầu trùng là nguyên nhân thực tế. Chưa thử đăng nhập/reboot trên máy đó.

Ngày 21/10/2026, Asia/Saigon. Máy thử: Windows 11 build 26200, x64; .NET SDK 9.0.303, Desktop Runtime 8.0.19.

## Trạng thái

Đã triển khai mã v3 trong phase 0–5 và tạo artifact Release. Không coi toàn bộ nghiệm thu vật lý đã hoàn tất: reboot/UAC/Win10 chưa kiểm tra trực tiếp; mục tiêu cold start <400 ms chưa đạt.

- Snapshot v2: c9aba78; branch remake-v3.
- Backup trước remake: config.pre-remake.20261021-091451.json trong thư mục config người dùng.
- Migration thật tạo config.v2.bak.json; config hiện tại version 3, giữ bốn action, startup off.
- Release: artifacts/release/QuickStartLauncher.exe, single-file, framework-dependent, ReadyToRun.
- Debug build: 0 warning, 0 error.
- Harness: **73 passed, 0 failed**. Kết quả/PNG: artifacts/tests/.
- Smoke Release cuối: **6 checks passed**, exit code 0. Evidence: artifacts/smoke-20261021-100620/.

## Đã kiểm chứng

| Phần | Bằng chứng |
|---|---|
| Migration v2 thật | Chrome/Facebook, Zalo, cmd start msteams:, Outlook; backup; version 3; discriminator duy nhất |
| JSON / persistence | Round trip, kind ở vị trí bất kỳ, 20 atomic saves, giữ file hỏng/khóa ghi, import recovery, từ chối version mới |
| Search | chrmoe, outlok, zalo, teams, coc coc, coccoc, CỐC CỐC, vsc |
| Browser | Nhận diện/loại PWA, Chromium profile/email, Firefox INI, quote URL, hai URL/new-window/profile, PowerShell multiline |
| Executor | Thứ tự, delay/delayAfter, countdown, continue-on-error, disabled/skip, cancellation, UAC 1223 giả lập, Store activation route, default URL handler route |
| Startup | Registry test key riêng: register/quote/self-heal/off/StartupApproved; toast cancel, run-now, thứ tự A→B, mở Launcher hủy countdown |
| AppsFolder | 142 ứng dụng thật, có Store, trích/freeze icon shell |
| UI / MVVM | Render Launcher/Config/picker/Settings/editor/gallery; light/dark; drawer; dropdown giữ profile; undo/reorder/duplicate/last-profile guard; multi-select giữ thứ tự; 0 binding warnings/errors |
| Accessibility | Palette text chính/phụ/accent light/dark đạt >=4.5:1; focus và nhãn accessibility rà trong XAML |
| Release process | --profile, second-process activation, auto-close, --autostart off, portable |
| Profile thật | Lệnh mở trả thành công cho 4 mục, launcher exit 0; có process chrome, Zalo, ms-teams, OUTLOOK |

Profile thật chạy lúc 09:50–09:51. Log: %LocalAppData%\QuickStart\logs\. Đây là xác nhận API mở/process tồn tại, chưa xác nhận nội dung cửa sổ/browser tab bằng automation desktop.

## Hiệu năng

| Hạng mục | Kết quả |
|---|---:|
| AppsFolder harness cuối | 955 ms / 142 app |
| AppsFolder Release smoke cuối | 500 ms / 142 app |
| Đọc inventory RAM | <1 ms |
| Picker lần đầu (render, chưa đợi mọi icon) | 243 ms |
| Picker lần hai (render) | 110 ms |
| Release process → first rendered window | **1247 ms** |
| Delay xong → process exit | **1250 ms** |

Các lần trước first-render khoảng 0.8–1.6s. Mục tiêu <400 ms chưa đạt. Đã dùng ReadyToRun và chuyển shell/icon/Process.Start sang STA nền.

## Chưa xác nhận trực tiếp

- Reboot/logon với Tự chạy / Chỉ mở Launcher; startup khi Explorer/mạng chưa sẵn.
- Bấm No ở UAC thật (đã test 1223 giả lập).
- Win10, RDP, transparency off, đổi theme Windows khi app mở.
- Hai URL trong một cửa sổ đúng profile trên từng browser; arguments/parser đã test.
- Đối chiếu toàn bộ Start Menu/icon, browse dialog/mọi phím/drag bằng input thật.
- Power-loss lúc ghi; dùng atomic replace và test nhiều lượt lưu, chưa gây mất điện thật.

Computer-use không khởi tạo được vì sandbox helper_unknown_error: setup refresh had errors. UI được kiểm tra bằng WPF harness và render trực tiếp, không giả định có desktop end-to-end test.

## Khác biệt triển khai

- COM shell khai báo ABI trực tiếp; CsWin32 sinh native declarations.
- PowerShell dùng EncodedCommand để giữ dấu nháy/nhiều dòng.
- Dùng package VirtualizingWrapPanel riêng.
- Backdrop đặt trước tạo HWND; DwmInterop cập nhật sau đó, tránh lỗi re-entrant WindowChrome gặp ở WPF-UI 4.3.0.
- Tên log ngày là yyyyMMdd.log theo Serilog rolling sink.
