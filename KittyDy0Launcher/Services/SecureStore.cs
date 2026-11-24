using System.Security.Cryptography;

namespace KittyDy0Launcher;

/// <summary>
/// Mã hóa dữ liệu nhỏ (token đăng nhập) để lưu ra file.
/// - Windows: DPAPI (chỉ đúng người dùng Windows này giải mã được), giống bản gốc.
/// - macOS/Linux: AES-256-CBC + HMAC-SHA256 (mã hóa rồi mới ký). Khóa ngẫu nhiên nằm ở ".store.key"
///   trong thư mục dữ liệu, quyền 600 (chỉ chủ tài khoản đọc được).
///   Đây là bảo vệ ở mức file, không mạnh bằng Keychain; nếu cần có thể chuyển sang Keychain sau.
/// </summary>
public static class SecureStore
{
    private static string KeyFile => Path.Combine(AppPaths.Root, ".store.key");

    public static byte[] Protect(byte[] data)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);

        var key = GetKey();
        var encKey = key.AsSpan(0, 32).ToArray();
        var macKey = key.AsSpan(32, 32).ToArray();

        using var aes = Aes.Create();
        aes.Key = encKey;
        aes.GenerateIV();
        var cipher = aes.EncryptCbc(data, aes.IV, PaddingMode.PKCS7);

        var body = new byte[16 + cipher.Length];          // iv | cipher
        aes.IV.CopyTo(body, 0);
        cipher.CopyTo(body, 16);
        var mac = HMACSHA256.HashData(macKey, body);

        var result = new byte[32 + body.Length];          // mac | iv | cipher
        mac.CopyTo(result, 0);
        body.CopyTo(result, 32);
        return result;
    }

    public static byte[] Unprotect(byte[] blob)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);

        if (blob.Length < 32 + 16 + 16) throw new CryptographicException("Dữ liệu mã hóa không hợp lệ.");
        var key = GetKey();
        var encKey = key.AsSpan(0, 32).ToArray();
        var macKey = key.AsSpan(32, 32).ToArray();

        var mac = blob.AsSpan(0, 32);
        var body = blob.AsSpan(32);
        var expected = HMACSHA256.HashData(macKey, body);
        if (!CryptographicOperations.FixedTimeEquals(mac, expected))
            throw new CryptographicException("Sai chữ ký (file hỏng hoặc của người dùng khác).");

        using var aes = Aes.Create();
        aes.Key = encKey;
        return aes.DecryptCbc(body[16..], body[..16], PaddingMode.PKCS7);
    }

    private static byte[] GetKey()
    {
        if (File.Exists(KeyFile))
        {
            var existing = File.ReadAllBytes(KeyFile);
            if (existing.Length == 64) return existing;
        }

        Directory.CreateDirectory(AppPaths.Root);
        var key = RandomNumberGenerator.GetBytes(64);
        var opts = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite // 600 ngay khi tạo
        };
        using (var fs = new FileStream(KeyFile, opts)) fs.Write(key);
        return key;
    }
}
