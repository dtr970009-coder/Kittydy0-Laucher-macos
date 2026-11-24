using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KittyDy0Launcher;

/// <summary>Lỗi đăng nhập có thông báo thân thiện, hiển thị thẳng cho người dùng.</summary>
public class AuthException : Exception
{
    public bool NeedsTwoFactor { get; }

    public AuthException(string message, bool needsTwoFactor = false) : base(message)
        => NeedsTwoFactor = needsTwoFactor;
}

/// <summary>Tài khoản Ely.by đã đăng nhập. Mật khẩu KHÔNG bao giờ được lưu, chỉ lưu token.</summary>
public sealed class ElyAccount
{
    public string Username { get; set; } = "";
    public string Uuid { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string ClientToken { get; set; } = "";
}

/// <summary>
/// Đăng nhập Ely.by qua API Yggdrasil (https://authserver.ely.by/auth).
/// Token được mã hóa (DPAPI trên Windows, AES+HMAC trên macOS/Linux, xem SecureStore) và lưu ở ely-account.dat.
/// </summary>
public class ElyByAuth
{
    private const string Api = "https://authserver.ely.by/auth";
    private static readonly HttpClient Http = CreateClient();
    private static string StoreFile => Path.Combine(AppPaths.Root, "ely-account.dat");

    private ElyAccount? _account = Load();

    public ElyAccount? Account => _account;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KittyDy0Launcher/1.0");
        return c;
    }

    // ---------- Lưu / đọc tài khoản (mã hóa, xem SecureStore) ----------

    private static ElyAccount? Load()
    {
        try
        {
            if (!File.Exists(StoreFile)) return null;
            var raw = SecureStore.Unprotect(File.ReadAllBytes(StoreFile));
            var acc = JsonSerializer.Deserialize<ElyAccount>(Encoding.UTF8.GetString(raw));
            return acc is { Username.Length: > 0, AccessToken.Length: > 0 } ? acc : null;
        }
        catch { return null; } // file hỏng hoặc của người dùng khác
    }

    private void Save(ElyAccount acc)
    {
        _account = acc;
        Directory.CreateDirectory(AppPaths.Root);
        var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(acc));
        File.WriteAllBytes(StoreFile, SecureStore.Protect(raw));
    }

    // ---------- API ----------

    private static async Task<(HttpStatusCode Status, JsonElement? Json)> PostAsync(string path, object body, CancellationToken ct)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await Http.PostAsync($"{Api}/{path}", content, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);

        JsonElement? json = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                json = doc.RootElement.Clone();
            }
            catch { /* phản hồi không phải JSON */ }
        }
        return (resp.StatusCode, json);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static AuthException ErrorFrom(HttpStatusCode status, JsonElement? json)
    {
        var msg = json is { ValueKind: JsonValueKind.Object } j ? Str(j, "errorMessage") : "";

        if (msg.Contains("two factor", StringComparison.OrdinalIgnoreCase))
            return new AuthException("Tài khoản bật xác thực 2 lớp. Nhập mã 6 số vào ô \"Mã 2FA\", nhập lại mật khẩu rồi đăng nhập lại.", true);
        if (msg.Contains("Invalid credentials", StringComparison.OrdinalIgnoreCase))
            return new AuthException("Sai tên đăng nhập/email hoặc mật khẩu Ely.by.");
        if ((int)status == 429)
            return new AuthException("Đăng nhập quá nhiều lần, hãy chờ một lúc rồi thử lại.");
        return new AuthException(msg.Length > 0 ? "Ely.by: " + msg : $"Ely.by trả về lỗi HTTP {(int)status}.");
    }

    /// <summary>Đăng nhập bằng tên đăng nhập/email + mật khẩu (+ mã 2FA nếu có).</summary>
    public async Task<ElyAccount> LoginAsync(string username, string password, string totp, CancellationToken ct)
    {
        var clientToken = Guid.NewGuid().ToString("N");
        // Ely.by nhận mã 2FA ghép sau mật khẩu, cách nhau dấu hai chấm
        var pw = totp.Length > 0 ? $"{password}:{totp}" : password;

        var (status, json) = await PostAsync("authenticate",
            new { username, password = pw, clientToken, requestUser = false }, ct);
        if ((int)status >= 400 || json is not { ValueKind: JsonValueKind.Object } root)
            throw ErrorFrom(status, json);

        var acc = ParseAccount(root, clientToken);
        Save(acc);
        return acc;
    }

    private static ElyAccount ParseAccount(JsonElement root, string fallbackClientToken)
    {
        if (!root.TryGetProperty("selectedProfile", out var prof) || prof.ValueKind != JsonValueKind.Object)
            throw new AuthException("Tài khoản Ely.by này chưa có nickname. Hãy tạo nickname trên ely.by rồi đăng nhập lại.");

        var token = Str(root, "accessToken");
        var uuid = Str(prof, "id");
        var name = Str(prof, "name");
        if (token.Length == 0 || uuid.Length == 0 || name.Length == 0)
            throw new AuthException("Ely.by trả về dữ liệu đăng nhập không đầy đủ.");

        var client = Str(root, "clientToken");
        return new ElyAccount
        {
            Username = name,
            Uuid = uuid,
            AccessToken = token,
            ClientToken = client.Length > 0 ? client : fallbackClientToken
        };
    }

    /// <summary>Kiểm tra token còn dùng được; hết hạn thì làm mới. Ném AuthException nếu phải đăng nhập lại.</summary>
    public async Task<ElyAccount> EnsureValidAsync(CancellationToken ct)
    {
        var acc = _account ?? throw new AuthException("Chưa đăng nhập Ely.by. Vào tab Tài khoản để đăng nhập.");

        var (vStatus, _) = await PostAsync("validate", new { accessToken = acc.AccessToken }, ct);
        if ((int)vStatus is >= 200 and < 300) return acc;

        var (rStatus, rJson) = await PostAsync("refresh",
            new { accessToken = acc.AccessToken, clientToken = acc.ClientToken }, ct);
        if ((int)rStatus is >= 200 and < 300 && rJson is { ValueKind: JsonValueKind.Object } root)
        {
            var fresh = ParseAccount(root, acc.ClientToken);
            Save(fresh);
            return fresh;
        }
        throw new AuthException("Phiên đăng nhập Ely.by đã hết hạn. Hãy đăng nhập lại ở tab Tài khoản.");
    }

    /// <summary>Đăng xuất: báo Ely.by hủy token (không bắt buộc thành công) rồi xóa file lưu.</summary>
    public async Task LogoutAsync()
    {
        var acc = _account;
        _account = null;
        try { if (File.Exists(StoreFile)) File.Delete(StoreFile); } catch { }

        if (acc == null) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await PostAsync("invalidate", new { accessToken = acc.AccessToken, clientToken = acc.ClientToken }, cts.Token);
        }
        catch { /* mất mạng thì thôi, token cục bộ đã bị xóa */ }
    }
}

/// <summary>
/// authlib-injector: java agent giúp game xác thực với Ely.by thay vì Mojang.
/// Tải một lần về cache rồi dùng lại.
/// </summary>
public static class AuthlibInjector
{
    private const string LatestUrl = "https://authlib-injector.yushi.moe/artifact/latest.json";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public static string JarPath => Path.Combine(AppPaths.Cache, "authlib-injector.jar");

    /// <summary>Đảm bảo có file jar, trả về đường dẫn.</summary>
    public static async Task<string> EnsureAsync(Action<string> log, CancellationToken ct)
    {
        if (File.Exists(JarPath) && new FileInfo(JarPath).Length > 0) return JarPath;

        try
        {
            log("Đang tải authlib-injector (dùng cho đăng nhập Ely.by)...");
            Directory.CreateDirectory(AppPaths.Cache);

            var json = await Http.GetStringAsync(LatestUrl, ct);
            using var doc = JsonDocument.Parse(json);
            var url = doc.RootElement.GetProperty("download_url").GetString();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("Link tải authlib-injector không hợp lệ.");

            string? sha256 = null;
            if (doc.RootElement.TryGetProperty("checksums", out var cs) &&
                cs.TryGetProperty("sha256", out var sh) && sh.ValueKind == JsonValueKind.String)
                sha256 = sh.GetString();

            var tmp = JarPath + ".tmp";
            using (var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var fs = File.Create(tmp);
                await src.CopyToAsync(fs, ct);
            }

            if (!string.IsNullOrEmpty(sha256))
            {
                string actual;
                using (var fs = File.OpenRead(tmp)) actual = Convert.ToHexString(SHA256.HashData(fs));
                if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tmp);
                    throw new InvalidDataException("authlib-injector tải về bị sai mã băm.");
                }
            }

            File.Move(tmp, JarPath, true);
            return JarPath;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new AuthException("Không tải được authlib-injector để đăng nhập Ely.by: " + ex.Message);
        }
    }
}
