using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KittyDy0Launcher;

/// <summary>Một phiên bản Minecraft trong manifest của Mojang.</summary>
public record VersionItem(string Id, string Type, DateTime Released, string Url);

public enum CatalogSource { BuiltIn, Network, Cache }

/// <summary>Danh sách phiên bản (release, snapshot, old_beta, old_alpha) và Java mà từng bản cần.</summary>
public class VersionCatalog
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static readonly string CachePath = Path.Combine(AppPaths.Cache, "version_manifest.json");

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KittyDy0Launcher/1.0");
        return c;
    }

    public List<VersionItem> All { get; private set; } = BuiltIn();

    public VersionItem? Find(string id) => All.FirstOrDefault(v => v.Id == id);

    /// <summary>Phiên bản đã tải đủ file jar vào thư mục dùng chung chưa.</summary>
    public static bool IsInstalled(string id) =>
        !string.IsNullOrEmpty(id) &&
        File.Exists(Path.Combine(AppPaths.Shared, "versions", id, id + ".jar"));

    public async Task<CatalogSource> LoadAsync()
    {
        string? json = null;
        var src = CatalogSource.BuiltIn;

        try
        {
            json = await Http.GetStringAsync(ManifestUrl);
            src = CatalogSource.Network;
            try
            {
                Directory.CreateDirectory(AppPaths.Cache);
                await File.WriteAllTextAsync(CachePath, json);
            }
            catch { }
        }
        catch
        {
            try
            {
                if (File.Exists(CachePath))
                {
                    json = await File.ReadAllTextAsync(CachePath);
                    src = CatalogSource.Cache;
                }
            }
            catch { }
        }

        var parsed = json is null ? null : Parse(json);
        if (parsed is { Count: > 0 }) All = parsed;
        else src = CatalogSource.BuiltIn;
        return src;
    }

    private static List<VersionItem>? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<VersionItem>();
            foreach (var v in doc.RootElement.GetProperty("versions").EnumerateArray())
            {
                var id = v.GetProperty("id").GetString();
                var type = v.GetProperty("type").GetString();
                var url = v.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var time = v.TryGetProperty("releaseTime", out var t) && t.TryGetDateTime(out var dt)
                    ? dt : DateTime.MinValue;
                if (id is not null && type is not null) list.Add(new VersionItem(id, type, time, url));
            }
            return list.OrderByDescending(x => x.Released).ToList(); // mới nhất → cổ nhất
        }
        catch { return null; }
    }

    private static List<VersionItem> BuiltIn() =>
        new[] { "1.21.4", "1.21.1", "1.20.6", "1.20.4", "1.20.1", "1.19.4", "1.18.2", "1.16.5", "1.12.2", "1.8.9", "1.7.10" }
        .Select(id => new VersionItem(id, "release", DateTime.MinValue, "")).ToList();

    /// <summary>Java tối thiểu cần cho phiên bản này (đọc từ file json của Mojang, có cache để dùng offline).</summary>
    public async Task<int> GetRequiredJavaMajorAsync(string id, CancellationToken ct = default)
    {
        var cacheFile = Path.Combine(AppPaths.Cache, $"javamajor_{id}.txt");
        try
        {
            if (File.Exists(cacheFile) && int.TryParse(File.ReadAllText(cacheFile).Trim(), out var cached))
                return cached;
        }
        catch { }

        var item = Find(id);
        if (item is { Url.Length: > 0 })
        {
            try
            {
                var json = await Http.GetStringAsync(item.Url, ct);
                using var doc = JsonDocument.Parse(json);
                var major = 8; // bản cũ không khai báo javaVersion thì dùng Java 8
                if (doc.RootElement.TryGetProperty("javaVersion", out var jv) &&
                    jv.TryGetProperty("majorVersion", out var mv) && mv.TryGetInt32(out var m))
                    major = m;

                try
                {
                    Directory.CreateDirectory(AppPaths.Cache);
                    File.WriteAllText(cacheFile, major.ToString());
                }
                catch { }
                return major;
            }
            catch (OperationCanceledException) { throw; }
            catch { /* rơi xuống đoán theo tên phiên bản */ }
        }
        return GuessJavaMajor(id);
    }

    /// <summary>Đoán Java theo tên phiên bản khi không đọc được manifest (offline lần đầu).</summary>
    public static int GuessJavaMajor(string id)
    {
        var m = Regex.Match(id, @"^1\.(\d+)(?:\.(\d+))?");
        if (m.Success)
        {
            var minor = int.Parse(m.Groups[1].Value);
            var patch = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
            if (minor >= 21 || (minor == 20 && patch >= 5)) return 21;
            if (minor >= 17) return 17;
            return 8;
        }
        if (Regex.IsMatch(id, @"^\d{2}w\d{2}")) return 21;     // snapshot kiểu 24w14a
        if (Regex.IsMatch(id, @"^\d{2}\.\d+")) return 25;       // đánh số phiên bản mới (26.x...)
        return 8;                                               // a1.x, b1.x, c0.x, inf-...
    }
}
