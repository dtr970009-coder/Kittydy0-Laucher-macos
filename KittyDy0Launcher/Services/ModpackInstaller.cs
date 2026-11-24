using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KittyDy0Launcher;

/// <summary>Loại modpack nhận diện được từ nội dung file (không dựa vào đuôi file).</summary>
public enum ModpackKind { Modrinth, CurseForge, Plain }

/// <summary>Một file cần tải về thư mục game, đường dẫn tính từ thư mục minecraft của instance.</summary>
public sealed record ModpackFile(string RelPath, List<string> Urls, string? Sha1, string? Sha512);

/// <summary>Một mod trong manifest CurseForge (chỉ có ID, cần API key để tra link tải).</summary>
public sealed record CurseFile(long ProjectId, long FileId);

public sealed class ModpackInfo
{
    public ModpackKind Kind { get; init; }
    public string Name { get; set; } = "";
    public string McVersion { get; set; } = "";
    public string LoaderType { get; set; } = "";     // fabric | quilt | forge | neoforge | ""
    public string LoaderVersion { get; set; } = "";
    public string OverridesDir { get; set; } = "overrides";
    public List<ModpackFile> Files { get; } = new();      // Modrinth
    public List<CurseFile> CurseFiles { get; } = new();   // CurseForge
}

public sealed class ModpackResult
{
    public int Installed { get; set; }
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Đọc và cài modpack:
/// - .mrpack (Modrinth): tải mod theo modrinth.index.json, kiểm tra mã băm, chép overrides.
/// - .zip CurseForge (manifest.json): chép overrides; tải mod nếu có CurseForgeApiKey trong config.
/// - .zip thường: giải nén thẳng vào thư mục game (tự nhận ra thư mục gốc).
/// </summary>
public class ModpackInstaller
{
    // Modrinth chỉ cho phép tải từ các máy chủ này; thêm CDN của CurseForge cho gói CurseForge.
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cdn.modrinth.com", "github.com", "raw.githubusercontent.com", "gitlab.com",
        "edge.forgecdn.net", "mediafilez.forgecdn.net"
    };

    // Tên thư mục/file quen thuộc dùng để đoán thư mục gốc của zip thường
    private static readonly HashSet<string> KnownTop = new(StringComparer.OrdinalIgnoreCase)
    {
        "mods", "config", "resourcepacks", "shaderpacks", "saves", "defaultconfigs",
        "kubejs", "scripts", "datapacks", "options.txt", "servers.dat"
    };

    private static readonly HttpClient Http = CreateClient();
    private readonly Action<string> _log;
    private readonly string _curseKey;

    public ModpackInstaller(Action<string> log, string? curseForgeApiKey)
    {
        _log = log;
        _curseKey = curseForgeApiKey?.Trim() ?? "";
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KittyDy0Launcher/1.0");
        return c;
    }

    // ================= Đọc thông tin =================

    /// <summary>Mở zip, nhận diện loại modpack và đọc thông tin. Ném lỗi nếu file hỏng.</summary>
    public static ModpackInfo Inspect(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);

        var mr = FindEntry(zip, "modrinth.index.json");
        if (mr != null) return ParseModrinth(mr);

        var cf = FindEntry(zip, "manifest.json");
        if (cf != null)
        {
            var info = TryParseCurse(cf);
            if (info != null) return info;
        }

        return new ModpackInfo { Kind = ModpackKind.Plain, Name = Path.GetFileNameWithoutExtension(zipPath) };
    }

    private static string Norm(ZipArchiveEntry e) => e.FullName.Replace('\\', '/');

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string name) =>
        zip.Entries.FirstOrDefault(e => Norm(e).Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static ModpackInfo ParseModrinth(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var doc = JsonDocument.Parse(s);
        var root = doc.RootElement;

        var info = new ModpackInfo { Kind = ModpackKind.Modrinth, Name = Str(root, "name") };

        if (!root.TryGetProperty("dependencies", out var deps) || deps.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("modrinth.index.json thiếu mục dependencies.");

        info.McVersion = Str(deps, "minecraft");
        if (info.McVersion.Length == 0)
            throw new InvalidDataException("modpack không ghi phiên bản Minecraft.");

        foreach (var (key, type) in new[]
                 { ("fabric-loader", "fabric"), ("quilt-loader", "quilt"), ("neoforge", "neoforge"), ("forge", "forge") })
        {
            var v = Str(deps, key);
            if (v.Length == 0) continue;
            info.LoaderType = type;
            info.LoaderVersion = v;
            break;
        }

        if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in files.EnumerateArray())
            {
                // Bỏ qua mod chỉ dành cho server
                if (f.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object &&
                    Str(env, "client") == "unsupported") continue;

                var path = Str(f, "path");
                if (path.Length == 0) continue;

                var urls = new List<string>();
                if (f.TryGetProperty("downloads", out var dl) && dl.ValueKind == JsonValueKind.Array)
                    foreach (var u in dl.EnumerateArray())
                        if (u.ValueKind == JsonValueKind.String && u.GetString() is { Length: > 0 } us) urls.Add(us);

                string? sha1 = null, sha512 = null;
                if (f.TryGetProperty("hashes", out var h) && h.ValueKind == JsonValueKind.Object)
                {
                    sha1 = Str(h, "sha1");
                    sha512 = Str(h, "sha512");
                    if (sha1.Length == 0) sha1 = null;
                    if (sha512.Length == 0) sha512 = null;
                }
                info.Files.Add(new ModpackFile(path, urls, sha1, sha512));
            }
        }
        return info;
    }

    /// <summary>manifest.json có thể là file khác không phải CurseForge, nên lỗi thì trả null (coi như zip thường).</summary>
    private static ModpackInfo? TryParseCurse(ZipArchiveEntry entry)
    {
        try
        {
            using var s = entry.Open();
            using var doc = JsonDocument.Parse(s);
            var root = doc.RootElement;
            if (!root.TryGetProperty("minecraft", out var mc) || mc.ValueKind != JsonValueKind.Object) return null;

            var info = new ModpackInfo
            {
                Kind = ModpackKind.CurseForge,
                Name = Str(root, "name"),
                McVersion = Str(mc, "version")
            };
            if (info.McVersion.Length == 0) return null;

            var ov = Str(root, "overrides");
            if (ov.Length > 0) info.OverridesDir = ov.Trim('/', '\\');

            if (mc.TryGetProperty("modLoaders", out var loaders) && loaders.ValueKind == JsonValueKind.Array)
            {
                // Lấy loader được đánh dấu primary, không có thì lấy cái đầu tiên
                string? chosen = null;
                foreach (var l in loaders.EnumerateArray())
                {
                    var id = Str(l, "id");
                    if (id.Length == 0) continue;
                    chosen ??= id;
                    if (l.TryGetProperty("primary", out var pr) && pr.ValueKind == JsonValueKind.True)
                    {
                        chosen = id;
                        break;
                    }
                }
                // id có dạng "forge-47.2.0", "fabric-0.14.21"
                var dash = chosen?.IndexOf('-') ?? -1;
                if (chosen != null && dash > 0)
                {
                    info.LoaderType = chosen[..dash].ToLowerInvariant();
                    info.LoaderVersion = chosen[(dash + 1)..];
                }
            }

            if (root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in files.EnumerateArray())
                {
                    if (f.TryGetProperty("projectID", out var p) && p.TryGetInt64(out var pid) &&
                        f.TryGetProperty("fileID", out var fi) && fi.TryGetInt64(out var fid))
                        info.CurseFiles.Add(new CurseFile(pid, fid));
                }
            }
            return info;
        }
        catch { return null; }
    }

    // ================= Cài đặt =================

    /// <summary>Tải mod và chép file cấu hình vào <paramref name="gameDir"/>.</summary>
    public async Task<ModpackResult> InstallAsync(string zipPath, ModpackInfo info, string gameDir,
        Action<double, string> report, CancellationToken ct)
    {
        var result = new ModpackResult();
        Directory.CreateDirectory(gameDir);

        switch (info.Kind)
        {
            case ModpackKind.Modrinth:
            {
                await DownloadAllAsync(info.Files, gameDir, report, 0, 85, null, ct);
                result.Installed = info.Files.Count;

                report(85, "Đang chép file cấu hình (overrides)...");
                // Theo chuẩn mrpack: overrides trước, client-overrides sau (ghi đè)
                result.Installed += await Task.Run(() =>
                {
                    using var zip = ZipFile.OpenRead(zipPath);
                    return ExtractFolder(zip, "overrides/", gameDir, ct) +
                           ExtractFolder(zip, "client-overrides/", gameDir, ct);
                }, ct);
                break;
            }

            case ModpackKind.CurseForge:
            {
                var files = await ResolveCurseFilesAsync(info, result, ct);
                var failures = new List<string>();
                await DownloadAllAsync(files, gameDir, report, 0, 85, failures, ct);
                result.Installed = files.Count - failures.Count;
                result.Warnings.AddRange(failures.Select(f => "Không tải được: " + f));

                report(85, "Đang chép file cấu hình (overrides)...");
                var prefix = info.OverridesDir.Trim('/') + "/";
                result.Installed += await Task.Run(() =>
                {
                    using var zip = ZipFile.OpenRead(zipPath);
                    return ExtractFolder(zip, prefix, gameDir, ct);
                }, ct);
                break;
            }

            default:
                report(10, "Đang giải nén file zip...");
                result.Installed = await Task.Run(() => ExtractPlain(zipPath, gameDir, ct), ct);
                break;
        }

        report(100, "Hoàn tất cài modpack.");
        return result;
    }

    // ---------- Giải nén ----------

    /// <summary>Chặn đường dẫn thoát ra ngoài thư mục game (kiểu "..\..\x" hoặc "C:\x").</summary>
    private static string SafeCombine(string root, string relative)
    {
        var rel = relative.Replace('\\', '/').TrimStart('/');
        var full = Path.GetFullPath(Path.Combine(root, rel));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Đường dẫn không hợp lệ trong modpack: " + relative);
        return full;
    }

    private static int ExtractFolder(ZipArchive zip, string prefix, string gameDir, CancellationToken ct)
    {
        var n = 0;
        foreach (var e in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(e.Name)) continue; // thư mục
            var full = Norm(e);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var dest = SafeCombine(gameDir, full[prefix.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, true);
            n++;
        }
        return n;
    }

    private int ExtractPlain(string zipPath, string gameDir, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var files = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name) && !Norm(e).StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var prefix = DetectRoot(files);
        if (prefix != null)
        {
            _log(prefix.Length == 0
                ? "Zip thường: giải nén từ thư mục gốc."
                : $"Zip thường: bỏ thư mục bọc ngoài \"{prefix}\".");
            return ExtractFolder(zip, prefix, gameDir, ct);
        }

        // Không thấy thư mục quen thuộc: nếu toàn file .jar thì coi là gói mod
        var jars = files.Where(e => e.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).ToList();
        if (jars.Count == 0)
            throw new InvalidDataException("Không nhận ra cấu trúc zip (không thấy mods, config, saves... hay file .jar).");

        _log($"Zip chỉ có file .jar: chép {jars.Count} file vào thư mục mods.");
        foreach (var e in jars)
        {
            ct.ThrowIfCancellationRequested();
            var dest = SafeCombine(gameDir, "mods/" + e.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            e.ExtractToFile(dest, true);
        }
        return jars.Count;
    }

    /// <summary>
    /// Tìm thư mục gốc của zip: phần đường dẫn đứng trước mods/config/saves...
    /// Trả về "" nếu các thư mục này nằm ngay ở gốc, null nếu không thấy gì.
    /// </summary>
    private static string? DetectRoot(List<ZipArchiveEntry> files)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in files)
        {
            var seg = Norm(e).Split('/');
            for (var i = 0; i < seg.Length; i++)
            {
                var isLast = i == seg.Length - 1;
                // Thư mục quen thuộc phải có nội dung bên trong; file quen thuộc phải là phần tử cuối
                var isFileName = KnownTop.Contains(seg[i]) && (seg[i].Contains('.') ? isLast : !isLast);
                if (!isFileName) continue;

                var prefix = i == 0 ? "" : string.Join("/", seg.Take(i)) + "/";
                counts[prefix] = counts.GetValueOrDefault(prefix) + 1;
                break;
            }
        }
        if (counts.Count == 0) return null;
        return counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key.Length).First().Key;
    }

    // ---------- Tải mod ----------

    private async Task DownloadAllAsync(List<ModpackFile> files, string gameDir,
        Action<double, string> report, double from, double to, List<string>? failures, CancellationToken ct)
    {
        if (files.Count == 0) return;

        var total = files.Count;
        var done = 0;
        Exception? firstError = null;
        var errLock = new object();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var gate = new SemaphoreSlim(4); // tải song song tối đa 4 file

        var tasks = files.Select(async f =>
        {
            await gate.WaitAsync(linked.Token);
            try
            {
                await DownloadOneAsync(f, gameDir, linked.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (failures == null)
                {
                    lock (errLock) { firstError ??= ex; }
                    linked.Cancel(); // một mod lỗi thì dừng cả gói
                    return;
                }
                lock (failures) failures.Add($"{f.RelPath} ({ex.Message})");
            }
            finally
            {
                gate.Release();
            }

            var n = Interlocked.Increment(ref done);
            report(from + (to - from) * n / total, $"Đang tải mod {n}/{total}: {Path.GetFileName(f.RelPath)}");
        }).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Nếu bị hủy do một mod lỗi thì ném đúng lỗi gốc cho người dùng đọc
            if (firstError != null) throw new IOException(firstError.Message, firstError);
            throw;
        }
        if (firstError != null) throw new IOException(firstError.Message, firstError);
    }

    private async Task DownloadOneAsync(ModpackFile f, string gameDir, CancellationToken ct)
    {
        var dest = SafeCombine(gameDir, f.RelPath);
        if (File.Exists(dest) && VerifyHash(dest, f)) return; // đã có file đúng, khỏi tải lại

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var tmp = dest + ".tmp";
        Exception? last = null;

        foreach (var url in f.Urls)
        {
            if (!IsAllowedUrl(url))
            {
                _log("Bỏ qua link tải không nằm trong danh sách cho phép: " + url);
                continue;
            }

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                    {
                        resp.EnsureSuccessStatusCode();
                        await using var src = await resp.Content.ReadAsStreamAsync(ct);
                        await using var fs = File.Create(tmp);
                        await src.CopyToAsync(fs, ct);
                    }
                    if (!VerifyHash(tmp, f)) throw new InvalidDataException("sai mã băm (file bị lỗi hoặc bị sửa)");
                    File.Move(tmp, dest, true);
                    return;
                }
                catch (OperationCanceledException) { try { File.Delete(tmp); } catch { } throw; }
                catch (Exception ex)
                {
                    last = ex;
                    try { File.Delete(tmp); } catch { }
                    if (attempt < 3) await Task.Delay(800 * attempt, ct);
                }
            }
        }

        throw new IOException($"Không tải được {f.RelPath}: {last?.Message ?? "không có link hợp lệ"}", last);
    }

    private static bool IsAllowedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        AllowedHosts.Contains(u.Host);

    /// <summary>Kiểm tra SHA-512 (ưu tiên) hoặc SHA-1. Không có mã băm thì coi như đạt.</summary>
    private static bool VerifyHash(string path, ModpackFile f)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (f.Sha512 != null)
                return Convert.ToHexString(SHA512.HashData(fs)).Equals(f.Sha512, StringComparison.OrdinalIgnoreCase);
            if (f.Sha1 != null)
                return Convert.ToHexString(SHA1.HashData(fs)).Equals(f.Sha1, StringComparison.OrdinalIgnoreCase);
            return true;
        }
        catch { return false; }
    }

    // ---------- CurseForge ----------

    /// <summary>Tra link tải của từng mod qua API CurseForge. Cần CurseForgeApiKey trong config.json.</summary>
    private async Task<List<ModpackFile>> ResolveCurseFilesAsync(ModpackInfo info, ModpackResult result, CancellationToken ct)
    {
        var list = new List<ModpackFile>();
        if (info.CurseFiles.Count == 0) return list;

        if (_curseKey.Length == 0)
        {
            result.Warnings.Add(
                $"Gói CurseForge có {info.CurseFiles.Count} mod nhưng chưa có API key nên launcher chưa tải được chúng. " +
                "Hãy điền \"CurseForgeApiKey\" trong config.json (lấy key miễn phí tại console.curseforge.com) rồi nhập lại, " +
                "hoặc chọn bản .mrpack của cùng modpack trên Modrinth.");
            return list;
        }

        foreach (var chunk in info.CurseFiles.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var body = JsonSerializer.Serialize(new { fileIds = chunk.Select(c => c.FileId).ToArray() });
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.curseforge.com/v1/mods/files")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Add("x-api-key", _curseKey);
            req.Headers.Accept.ParseAdd("application/json");

            using var resp = await Http.SendAsync(req, ct);
            if (resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new InvalidOperationException("API key CurseForge không hợp lệ.");
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var found = new HashSet<long>();

            foreach (var d in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var id = d.GetProperty("id").GetInt64();
                var name = Path.GetFileName(Str(d, "fileName"));
                if (name.Length == 0) continue;
                found.Add(id);

                // downloadUrl có thể null nếu tác giả tắt phân phối bên thứ ba: thử đường dẫn CDN theo quy ước
                var url = Str(d, "downloadUrl");
                if (url.Length == 0)
                    url = $"https://edge.forgecdn.net/files/{id / 1000}/{id % 1000}/{Uri.EscapeDataString(name)}";

                string? sha1 = null;
                if (d.TryGetProperty("hashes", out var hs) && hs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var h in hs.EnumerateArray())
                        if (h.TryGetProperty("algo", out var algo) && algo.TryGetInt32(out var a) && a == 1)
                            sha1 = Str(h, "value");
                }
                if (string.IsNullOrEmpty(sha1)) sha1 = null;

                // API này không cho biết loại; .jar là mod, còn lại để vào resourcepacks
                var folder = name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ? "mods" : "resourcepacks";
                list.Add(new ModpackFile($"{folder}/{name}", new List<string> { url }, sha1, null));
            }

            foreach (var c in chunk.Where(c => !found.Contains(c.FileId)))
                result.Warnings.Add($"CurseForge không trả về file (project {c.ProjectId}, file {c.FileId}).");
        }
        return list;
    }
}
