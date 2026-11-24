# KittyDy0 Launcher trên macOS (giai đoạn 1: phần lõi)

Cấu trúc:
- `KittyDy0Launcher/`      bản Windows (WPF) gốc, đã sửa phần lõi để dùng chung với Mac
- `KittyDy0Launcher.Core/` phần lõi net8.0 chạy mọi hệ điều hành (liên kết file từ `KittyDy0Launcher/Services`, không copy)
- `KittyDy0Launcher.Cli/`  bản dòng lệnh `kittydy0` để thử trên Mac
- `.github/workflows/mac-build.yml` build trên macOS (arm64 và x64), tải file ở tab Actions > Artifacts

Dùng thử trên Mac (không cần cài .NET vì bản publish đã kèm runtime):
    tar -xzf kittydy0-osx-arm64.tar.gz
    xattr -dr com.apple.quarantine kittydy0     # file chưa ký nên Gatekeeper sẽ chặn
    ./kittydy0 info
    ./kittydy0 java 21
    ./kittydy0 play 1.21.1 Steve

Đã đổi cho Mac: thư mục dữ liệu (~/Library/Application Support/KittyDy0Launcher), java thay cho java.exe,
tải Java dạng tar.gz đúng CPU (Apple Silicon/Intel), tìm Java trong /Library/Java/JavaVirtualMachines và Homebrew,
mã hóa token Ely.by (AES+HMAC, khóa quyền 600), mở thư mục/link bằng `open`.

Chưa làm: giao diện (Avalonia), đăng ký file .mrpack (cần Info.plist), chạy một bản duy nhất, đóng gói .app/.dmg.
Lưu ý: phiên bản Minecraft cũ (<= 1.12, LWJGL 2) không có bản native cho Apple Silicon.
