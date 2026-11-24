using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;

namespace KittyDy0Launcher;

/// <summary>
/// Đăng nhập Microsoft/Xbox bằng JELoginHandler của CmlLib.Core.Auth.Microsoft.
/// Mọi lời gọi tới thư viện đều nằm trong file này: nếu API của bản NuGet bạn cài khác,
/// chỉ cần sửa ở đây. Thư viện tự lưu và làm mới token trong ms-accounts.json.
/// Không có secret nào được ghi trong code.
/// </summary>
public class MicrosoftAuth
{
    private readonly JELoginHandler _handler;

    public MicrosoftAuth()
    {
        Directory.CreateDirectory(AppPaths.Root);
        _handler = new JELoginHandlerBuilder()
            .WithAccountManager(Path.Combine(AppPaths.Root, "ms-accounts.json"))
            .Build();
    }

    /// <summary>Đăng nhập không cần hỏi lại (dùng token đã lưu, tự làm mới). Trả về null nếu phải đăng nhập lại.</summary>
    public async Task<MSession?> TrySilentAsync()
    {
        try { return await _handler.AuthenticateSilently(); }
        catch { return null; }
    }

    /// <summary>Mở cửa sổ đăng nhập Microsoft. Ném lỗi nếu người dùng đóng cửa sổ, mất mạng, hoặc không sở hữu game.</summary>
    public async Task<MSession> SignInAsync() => await _handler.AuthenticateInteractively();

    /// <summary>Xóa tài khoản đã lưu.</summary>
    public async Task SignOutAsync() => await _handler.Signout();
}
