using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KittyDy0Launcher;

/// <summary>Lỗi có thông báo thân thiện để hiện thẳng cho người dùng.</summary>
public class ModException : Exception
{
    public ModException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Loại nội dung tải về: Mod, Resource Pack hoặc Modpack.</summary>
public enum ModKind { Mod, ResourcePack, Modpack }

/// <summary>Các hàm phụ cho phiên bản Minecraft dạng "1.20.1".</summary>
internal static class McVersions
{
    private static readonly Regex Release = new(@"^\d+\.\d+(\.\d+)?$");

    /// <summary>Chỉ giữ bản phát hành (bỏ snapshot), mới nhất lên đầu.</summary>
    public static List<string> SortedReleases(IEnumerable<string> versions) =>
        versions.Where(v => Release.IsMatch(v)).Distinct().OrderByDescending(v => Version.Parse(v)).ToList();

    /// <summary>"1.16.5 – 1.21.4" hoặc "1.20.1"; rỗng nếu không có bản phát hành nào.</summary>
    public static string Range(IEnumerable<string> versions)
    {
        var l = SortedReleases(versions);
        return l.Count == 0 ? "" : l.Count == 1 ? l[0] : $"{l[^1]} – {l[0]}";
    }
}

/// <summary>Một mục trong kết quả tìm kiếm, kèm dữ liệu để kiểm tra có hợp với instance đang chọn không.</summary>
public sealed partial class ModHit : ObservableObject
{
    private static readonly string[] KnownLoaders = { "fabric", "quilt", "forge", "neoforge" };

    public string Source { get; init; } = "";        // "Modrinth" | "CurseForge"
    public ModKind Kind { get; init; }
    public string Id { get; init; } = "";            // project id (Modrinth) hoặc mod id (CurseForge)
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public long Downloads { get; init; }
    public long Follows { get; init; }
    public DateTimeOffset? Updated { get; init; }
    public string? PageUrl { get; init; }
    public string? IconUrl { get; init; }

    // Dữ liệu tương thích: Modrinth cho danh sách phiên bản và loader riêng; CurseForge cho từng cặp (phiên bản, loader)
    public HashSet<string> Versions { get; init; } = new();
    public HashSet<string> Loaders { get; init; } = new();
    public List<(string Mc, string Loader)>? Builds { get; init; }

    // ---------- Cảnh báo ----------

    private string _warning = "";

    /// <summary>Cảnh báo khi không hợp với instance đang chọn; rỗng nếu phù hợp (hoặc không đủ dữ liệu để kết luận).</summary>
    public string Warning
    {
        get => _warning;
        set { if (SetProperty(ref _warning, value)) OnPropertyChanged(nameof(HasWarning)); }
    }

    public bool HasWarning => _warning.Length > 0;

    // ---------- Hiển thị ----------

    public string ActionText => Kind == ModKind.Modpack ? "Tải & nhập" : "Tải về";

    public string Subtitle => $"{(Author.Length > 0 ? "bởi " + Author + " · " : "")}{DownloadsText} · {Source}";

    public string DownloadsText =>
        Downloads >= 1_000_000 ? $"{Downloads / 1_000_000.0:0.#}M lượt tải" :
        Downloads >= 1_000 ? $"{Downloads / 1_000.0:0.#}K lượt tải" :
        $"{Downloads} lượt tải";

    /// <summary>Dòng thông tin ngắn: ngày cập nhật, phiên bản Minecraft hỗ trợ, loader.</summary>
    public string InfoLine
    {
        get
        {
            var parts = new List<string>();
            if (Updated is { } u) parts.Add("Cập nhật " + u.LocalDateTime.ToString("dd/MM/yyyy"));

            var mc = McVersions.Range(Builds != null ? Builds.Select(b => b.Mc) : Versions);
            if (mc.Length > 0) parts.Add("Minecraft " + mc);

            if (Kind == ModKind.Mod || Kind == ModKind.Modpack)
            {
                var loaders = Builds != null ? Builds.Select(b => b.Loader).Where(l => l.Length > 0) : Loaders;
                var names = loaders.Distinct().Select(LoaderInstaller.Label).ToList();
                if (names.Count > 0) parts.Add(string.Join(", ", names));
            }
            return string.Join(" · ", parts);
        }
    }

    // ---------- Kiểm tra tương thích ----------

    public static IEnumerable<string> OnlyLoaders(IEnumerable<string> names) =>
        names.Select(n => n.ToLowerInvariant()).Where(n => KnownLoaders.Contains(n));

    public static string LoaderName(int curseLoaderType) => curseLoaderType switch
    {
        1 => "forge", 4 => "fabric", 5 => "quilt", 6 => "neoforge", _ => ""
    };

    /// <summary>Trả về câu cảnh báo nếu không có bản cho Minecraft (và loader với mod) này, rỗng nếu phù hợp.</summary>
    public string CheckCompat(string mc, string loader)
    {
        if (Kind == ModKind.Modpack) return ""; // modpack tạo instance riêng, không phải cài vào instance đang chọn

        var needLoader = Kind == ModKind.Mod && loader.Length > 0;
        var accepted = loader == "quilt" ? new[] { "quilt", "fabric" } : new[] { loader }; // Quilt chạy được mod Fabric
        var loaderLabel = LoaderInstaller.Label(loader);

        if (Builds != null) // CurseForge
        {
            if (Builds.Count == 0) return "";
            if (!Builds.Any(b => b.Mc == mc)) return $"Không có bản cho Minecraft {mc}.";
            if (!needLoader) return "";
            if (Builds.Any(b => b.Mc == mc && (b.Loader == "" || accepted.Contains(b.Loader)))) return "";
            var have = Builds.Where(b => b.Mc == mc && b.Loader != "").Select(b => LoaderInstaller.Label(b.Loader)).Distinct();
            return $"Không có bản {loaderLabel} cho Minecraft {mc} (chỉ có: {string.Join(", ", have)}).";
        }

        // Modrinth
        if (Versions.Count > 0 && !Versions.Contains(mc)) return $"Không có bản cho Minecraft {mc}.";
        if (needLoader && Loaders.Count > 0 && !Loaders.Overlaps(accepted))
            return $"Không hỗ trợ {loaderLabel} (mod dành cho: {string.Join(", ", Loaders.Select(LoaderInstaller.Label))}).";
        return "";
    }
}

public sealed record ModSearchResult(List<ModHit> Hits, int Total);

/// <summary>Một phiên bản (file) có thể cài của một mod, resource pack hoặc modpack.</summary>
public sealed class ModVersionInfo
{
    public string Id { get; init; } = "";                  // Modrinth: id phiên bản; CurseForge: id file
    public string Name { get; init; } = "";
    public DateTimeOffset? Date { get; init; }
    public string Type { get; init; } = "release";         // release | beta | alpha
    public List<string> McVersions { get; init; } = new(); // bản phát hành Minecraft hỗ trợ, mới nhất trước
    public List<string> Loaders { get; init; } = new();    // fabric/forge/... (rỗng với resource pack)

    /// <summary>Dòng hiển thị trong ô chọn phiên bản: "1.12.2 · Forge · zombie-1.3 · 12/05/2019".</summary>
    public string Display
    {
        get
        {
            var parts = new List<string>();
            var mc = McVersions.Count == 0 ? "?" : McVersions.Count <= 3 ? string.Join(", ", McVersions) : McVersions[^1] + " – " + McVersions[0];
            parts.Add("MC " + mc);
            if (Loaders.Count > 0) parts.Add(string.Join("/", Loaders.Select(LoaderInstaller.Label)));
            if (Name.Length > 0) parts.Add(Name.Length > 36 ? Name[..36] + "…" : Name);
            if (Date is { } d) parts.Add(d.LocalDateTime.ToString("dd/MM/yyyy"));
            if (Type != "release") parts.Add("[" + Type + "]");
            return string.Join(" · ", parts);
        }
    }
}
public sealed record ModDetailRow(string Label, string Value);

/// <summary>Thông tin chi tiết hiển thị trong cửa sổ "Chi tiết".</summary>
public sealed class ModDetail
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? PageUrl { get; init; }
    public string Changelog { get; init; } = "";
    public string Error { get; init; } = "";
    public List<ModDetailRow> Rows { get; } = new();

    public bool HasChangelog => Changelog.Length > 0;
    public bool HasError => Error.Length > 0;
    public bool HasPageUrl => !string.IsNullOrEmpty(PageUrl);
}

public sealed class ModInstallResult
{
    public List<string> Installed { get; } = new();
    public List<string> Warnings { get; } = new();
}

/// <summary>
/// Tìm, xem chi tiết và cài mod, resource pack, modpack từ Modrinth (không cần key) và CurseForge
/// (cần CurseForgeApiKey trong config.json). Mod/resource pack được lọc theo đúng phiên bản Minecraft
/// (và loader với mod) của instance; mod tự cài kèm các mod phụ thuộc bắt buộc.
/// </summary>
public class ModBrowser
{
    public const int PageSize = 20;

    private const string ModrinthApi = "https://api.modrinth.com/v2";
    private const string CurseApi = "https://api.curseforge.com/v1";
    private const int CurseGameId = 432;   // Minecraft
    private const int MaxDepth = 4;        // độ sâu tối đa khi cài mod phụ thuộc

    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly HttpClient Http = CreateClient();
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cdn.modrinth.com", "edge.forgecdn.net", "mediafilez.forgecdn.net"
    };

    private readonly Action<string> _log;
    private readonly string _curseKey;

    public ModBrowser(Action<string> log, string? curseForgeApiKey)
    {
        _log = log;
        _curseKey = curseForgeApiKey?.Trim() ?? "";
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KittyDy0Launcher/1.0 (minecraft launcher)");
        return c;
    }

    // ================= Tiện ích =================

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;

    private static List<string> Strings(JsonElement e, string name)
    {
        var list = new List<string>();
        if (e.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var x in arr.EnumerateArray())
                if (x.ValueKind == JsonValueKind.String && x.GetString() is { Length: > 0 } s) list.Add(s);
        return list;
    }

    private static DateTimeOffset? Date(JsonElement e, string name) =>
        DateTimeOffset.TryParse(Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static string FormatDate(JsonElement e, string name) => Date(e, name)?.LocalDateTime.ToString("dd/MM/yyyy") ?? "";

    private static string FormatSize(long bytes) =>
        bytes >= 1_048_576 ? $"{bytes / 1_048_576.0:0.#} MB" : bytes >= 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes} B";

    private static string ModrinthType(ModKind k) => k switch
    {
        ModKind.ResourcePack => "resourcepack", ModKind.Modpack => "modpack", _ => "mod"
    };

    private static int CurseClass(ModKind k) => k switch
    {
        ModKind.ResourcePack => 12, ModKind.Modpack => 4471, _ => 6
    };

    /// <summary>Modrinth: Quilt chạy được cả mod Fabric nên tìm cả hai.</summary>
    private static string[] ModrinthLoaders(string loader) => loader == "quilt" ? new[] { "quilt", "fabric" } : new[] { loader };

    /// <summary>CurseForge modLoaderType: 1 Forge, 4 Fabric, 5 Quilt, 6 NeoForge. Quilt dùng mod Fabric.</summary>
    private static int CurseLoader(string loader) => loader switch
    {
        "forge" => 1, "fabric" => 4, "quilt" => 4, "neoforge" => 6, _ => 0
    };

    /// <summary>Bỏ thẻ HTML/markdown cơ bản để hiện thông tin cập nhật dạng chữ thường, cắt bớt cho gọn.</summary>
    private static string CleanText(string raw, int max)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var t = Regex.Replace(raw, @"<br\s*/?>|</p>|</li>|</h\d>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, "<[^>]+>", "");
        t = System.Net.WebUtility.HtmlDecode(t);
        t = Regex.Replace(t, @"^[#>\s]*", "", RegexOptions.Multiline);
        t = t.Replace("**", "").Replace("__", "").Replace("`", "");
        t = Regex.Replace(t, @"[ \t]+", " ");
        t = Regex.Replace(t, @"\n{3,}", "\n\n").Trim();
        return t.Length > max ? t[..max].TrimEnd() + "…" : t;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, bool curse, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (curse) req.Headers.Add("x-api-key", _curseKey);
        req.Headers.Accept.ParseAdd("application/json");

        HttpResponseMessage resp;
        try { resp = await Http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new ModException("Không kết nối được máy chủ. Kiểm tra Internet rồi thử lại.", ex); }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ModException("Máy chủ phản hồi quá lâu. Thử lại sau.", ex);
        }

        using (resp)
        {
            if (curse && resp.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new ModException("API key CurseForge không hợp lệ. Kiểm tra \"CurseForgeApiKey\" trong config.json.");
            if ((int)resp.StatusCode == 429)
                throw new ModException("Máy chủ đang giới hạn tốc độ. Chờ một chút rồi thử lại.");
            if (!resp.IsSuccessStatusCode)
                throw new ModException($"Máy chủ trả về lỗi HTTP {(int)resp.StatusCode}.");

            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        }
    }

    private void RequireCurseKey()
    {
        if (_curseKey.Length == 0)
            throw new ModException("CurseForge cần API key. Điền \"CurseForgeApiKey\" trong config.json (key miễn phí tại console.curseforge.com).");
    }

    // ================= Tìm kiếm (có phân trang) =================

    /// <summary>
    /// Tìm theo loại và trang (bắt đầu từ 1). Không lọc theo instance: danh sách luôn hiện; việc hợp hay không
    /// được kiểm tra riêng bằng ModHit.CheckCompat để hiện cảnh báo.
    /// </summary>
    public async Task<ModSearchResult> SearchAsync(string source, ModKind kind, string query, int page,
        string? mc, string? loader, CancellationToken ct)
    {
        page = Math.Max(1, page);
        return source == "CurseForge"
            ? await SearchCurseAsync(kind, query, page, mc, loader, ct)
            : await SearchModrinthAsync(kind, query, page, mc, loader, ct);
    }

    private async Task<ModSearchResult> SearchModrinthAsync(ModKind kind, string query, int page, string? mc, string? loader, CancellationToken ct)
    {
        var type = ModrinthType(kind);
        var facets = new List<string[]> { new[] { "project_type:" + type } };
        if (!string.IsNullOrEmpty(mc)) facets.Add(new[] { "versions:" + mc });
        if (kind == ModKind.Mod && !string.IsNullOrEmpty(loader))
            facets.Add(ModrinthLoaders(loader).Select(l => "categories:" + l).ToArray());
        var url = $"{ModrinthApi}/search?query={Uri.EscapeDataString(query)}" +
                  $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}" +
                  $"&limit={PageSize}&offset={(page - 1) * PageSize}&index={(query.Length == 0 ? "downloads" : "relevance")}";

        using var doc = await GetJsonAsync(url, false, ct);
        var root = doc.RootElement;
        var list = new List<ModHit>();
        foreach (var h in root.GetProperty("hits").EnumerateArray())
        {
            var slug = Str(h, "slug");
            list.Add(new ModHit
            {
                Source = "Modrinth",
                Kind = kind,
                Id = Str(h, "project_id"),
                Title = Str(h, "title"),
                Author = Str(h, "author"),
                Description = Str(h, "description"),
                Downloads = Num(h, "downloads"),
                Follows = Num(h, "follows"),
                Updated = Date(h, "date_modified"),
                PageUrl = slug.Length > 0 ? $"https://modrinth.com/{type}/{slug}" : null,
                IconUrl = Str(h, "icon_url") is { Length: > 0 } ic ? ic : null,
                Versions = new HashSet<string>(Strings(h, "versions")),
                Loaders = new HashSet<string>(ModHit.OnlyLoaders(Strings(h, "categories")))
            });
        }
        return new ModSearchResult(list, (int)Math.Min(Num(root, "total_hits"), int.MaxValue));
    }

    private async Task<ModSearchResult> SearchCurseAsync(ModKind kind, string query, int page, string? mc, string? loader, CancellationToken ct)
    {
        RequireCurseKey();
        var url = $"{CurseApi}/mods/search?gameId={CurseGameId}&classId={CurseClass(kind)}" +
                  $"&searchFilter={Uri.EscapeDataString(query)}&sortField={(query.Length == 0 ? 6 : 2)}&sortOrder=desc" +
                  $"&pageSize={PageSize}&index={(page - 1) * PageSize}";
        if (!string.IsNullOrEmpty(mc)) url += "&gameVersion=" + Uri.EscapeDataString(mc);
        if (kind == ModKind.Mod && !string.IsNullOrEmpty(loader) && CurseLoader(loader) != 0) url += "&modLoaderType=" + CurseLoader(loader);

        using var doc = await GetJsonAsync(url, true, ct);
        var list = new List<ModHit>();
        foreach (var m in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var author = "";
            if (m.TryGetProperty("authors", out var au) && au.ValueKind == JsonValueKind.Array && au.GetArrayLength() > 0)
                author = Str(au[0], "name");

            string? icon = null, site = null;
            if (m.TryGetProperty("logo", out var lg) && lg.ValueKind == JsonValueKind.Object)
                icon = Str(lg, "thumbnailUrl") is { Length: > 0 } t ? t : null;
            if (m.TryGetProperty("links", out var lk) && lk.ValueKind == JsonValueKind.Object)
                site = Str(lk, "websiteUrl") is { Length: > 0 } w ? w : null;

            // latestFilesIndexes: các cặp (phiên bản Minecraft, loader) mà mod có file
            var builds = new List<(string, string)>();
            if (m.TryGetProperty("latestFilesIndexes", out var idx) && idx.ValueKind == JsonValueKind.Array)
            {
                foreach (var i in idx.EnumerateArray())
                {
                    var gv = Str(i, "gameVersion");
                    if (gv.Length == 0) continue;
                    var loaderName = i.TryGetProperty("modLoader", out var ml) && ml.TryGetInt32(out var mt)
                        ? ModHit.LoaderName(mt) : "";
                    builds.Add((gv, loaderName));
                }
            }

            list.Add(new ModHit
            {
                Source = "CurseForge",
                Kind = kind,
                Id = Num(m, "id").ToString(),
                Title = Str(m, "name"),
                Author = author,
                Description = Str(m, "summary"),
                Downloads = Num(m, "downloadCount"),
                Updated = Date(m, "dateModified"),
                PageUrl = site,
                IconUrl = icon,
                Builds = builds
            });
        }

        var total = 0;
        if (doc.RootElement.TryGetProperty("pagination", out var pg) && pg.ValueKind == JsonValueKind.Object)
            total = (int)Math.Min(Num(pg, "totalCount"), int.MaxValue);
        return new ModSearchResult(list, total);
    }

    // ================= Danh sách phiên bản =================

    /// <summary>Liệt kê các phiên bản của một mục (mới nhất trước, tối đa 80) để người dùng chọn đúng bản muốn cài.</summary>
    public async Task<List<ModVersionInfo>> GetVersionsAsync(ModHit hit, CancellationToken ct)
    {
        var list = new List<ModVersionInfo>();

        if (hit.Source == "CurseForge")
        {
            RequireCurseKey();
            using var doc = await GetJsonAsync($"{CurseApi}/mods/{hit.Id}/files?pageSize=50", true, ct);
            foreach (var f in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                if (f.TryGetProperty("isAvailable", out var av) && av.ValueKind == JsonValueKind.False) continue;
                var gv = Strings(f, "gameVersions");
                var dn = Str(f, "displayName");
                list.Add(new ModVersionInfo
                {
                    Id = Num(f, "id").ToString(),
                    Name = dn.Length > 0 ? dn : Str(f, "fileName"),
                    Date = Date(f, "fileDate"),
                    Type = Num(f, "releaseType") switch { 1 => "release", 2 => "beta", _ => "alpha" },
                    McVersions = McVersions.SortedReleases(gv),
                    Loaders = ModHit.OnlyLoaders(gv).Distinct().ToList()
                });
            }
            return list.OrderByDescending(v => v.Date ?? DateTimeOffset.MinValue).Take(80).ToList();
        }

        using var vdoc = await GetJsonAsync($"{ModrinthApi}/project/{Uri.EscapeDataString(hit.Id)}/version", false, ct);
        foreach (var v in vdoc.RootElement.EnumerateArray())
        {
            var games = Strings(v, "game_versions");
            var rel = McVersions.SortedReleases(games);
            list.Add(new ModVersionInfo
            {
                Id = Str(v, "id"),
                Name = Str(v, "version_number"),
                Date = Date(v, "date_published"),
                Type = Str(v, "version_type") is { Length: > 0 } t ? t : "release",
                McVersions = rel.Count > 0 ? rel : games.Take(2).ToList(),
                Loaders = ModHit.OnlyLoaders(Strings(v, "loaders")).Distinct().ToList()
            });
            if (list.Count >= 80) break;
        }
        return list;
    }

    // ================= Chi tiết =================

    /// <summary>Lấy thông tin chi tiết: phiên bản mới nhất, ngày cập nhật, hỗ trợ, thông tin cập nhật (changelog)...</summary>
    /// <param name="mc">Phiên bản Minecraft của instance đang chọn (null nếu không có), dùng để tìm bản phù hợp.</param>
    /// <param name="loader">Loader của instance (null nếu chưa có).</param>
    public async Task<ModDetail> GetDetailAsync(ModHit hit, string? mc, string? loader, CancellationToken ct)
    {
        return hit.Source == "CurseForge"
            ? await DetailCurseAsync(hit, mc, loader, ct)
            : await DetailModrinthAsync(hit, mc, loader, ct);
    }

    private async Task<ModDetail> DetailModrinthAsync(ModHit hit, string? mc, string? loader, CancellationToken ct)
    {
        using var projDoc = await GetJsonAsync($"{ModrinthApi}/project/{Uri.EscapeDataString(hit.Id)}", false, ct);
        var p = projDoc.RootElement;

        // Bản phù hợp với instance (nếu có), không có thì bản mới nhất
        JsonElement? ver = null;
        var matched = false;
        var canMatch = mc != null && hit.Kind != ModKind.Modpack && (hit.Kind == ModKind.ResourcePack || loader != null);
        if (canMatch)
        {
            var loaders = hit.Kind == ModKind.Mod ? ModrinthLoaders(loader!) : null;
            ver = await PickModrinthVersionAsync(hit.Id, mc, loaders, ct);
            matched = ver != null;
        }
        ver ??= await PickModrinthVersionAsync(hit.Id, null, null, ct);

        var d = new ModDetail
        {
            Title = Str(p, "title"),
            Description = Str(p, "description"),
            PageUrl = hit.PageUrl,
            Changelog = ver != null ? CleanText(Str(ver.Value, "changelog"), 900) : ""
        };

        if (ver is { } v)
        {
            var num = Str(v, "version_number");
            var name = Str(v, "name");
            d.Rows.Add(new ModDetailRow(matched ? "Phiên bản phù hợp" : "Phiên bản mới nhất",
                name.Length > 0 && name != num ? $"{num} ({name})" : num));
            if (FormatDate(v, "date_published") is { Length: > 0 } dp) d.Rows.Add(new ModDetailRow("Ngày phát hành bản này", dp));
            if (PrimaryFile(v) is { } pf)
                d.Rows.Add(new ModDetailRow("Tệp tải", $"{Str(pf, "filename")} ({FormatSize(Num(pf, "size"))})"));
            var vMc = McVersions.Range(Strings(v, "game_versions"));
            if (vMc.Length > 0) d.Rows.Add(new ModDetailRow("Bản này cho Minecraft", vMc));
        }

        if (FormatDate(p, "updated") is { Length: > 0 } up) d.Rows.Add(new ModDetailRow("Cập nhật dự án", up));

        var all = McVersions.SortedReleases(Strings(p, "game_versions"));
        if (all.Count > 0)
        {
            var text = string.Join(", ", all);
            d.Rows.Add(new ModDetailRow("Hỗ trợ Minecraft", text.Length > 240 ? text[..240] + "…" : text));
        }
        var ld = ModHit.OnlyLoaders(Strings(p, "loaders")).Select(LoaderInstaller.Label).ToList();
        if (ld.Count > 0 && hit.Kind != ModKind.ResourcePack) d.Rows.Add(new ModDetailRow("Mod loader", string.Join(", ", ld)));

        d.Rows.Add(new ModDetailRow("Lượt tải", Num(p, "downloads").ToString("N0", Vi)));
        d.Rows.Add(new ModDetailRow("Theo dõi", Num(p, "followers").ToString("N0", Vi)));
        if (p.TryGetProperty("license", out var lic) && lic.ValueKind == JsonValueKind.Object)
        {
            var licName = Str(lic, "name").Length > 0 ? Str(lic, "name") : Str(lic, "id");
            if (licName.Length > 0) d.Rows.Add(new ModDetailRow("Giấy phép", licName));
        }
        var cats = Strings(p, "categories").Where(c => !ModHit.OnlyLoaders(new[] { c }).Any()).ToList();
        if (cats.Count > 0) d.Rows.Add(new ModDetailRow("Thể loại", string.Join(", ", cats)));

        if (canMatch) d.Rows.Add(CompatRow(hit.Kind, mc!, loader, matched));
        return d;
    }

    private static ModDetailRow CompatRow(ModKind kind, string mc, string? loader, bool matched)
    {
        var need = $"Minecraft {mc}" + (kind == ModKind.Mod && loader != null ? $" + {LoaderInstaller.Label(loader)}" : "");
        return new ModDetailRow("Với instance đang chọn", (matched ? "Có bản cho " : "Không có bản cho ") + need);
    }

    private async Task<ModDetail> DetailCurseAsync(ModHit hit, string? mc, string? loader, CancellationToken ct)
    {
        RequireCurseKey();
        using var doc = await GetJsonAsync($"{CurseApi}/mods/{hit.Id}", true, ct);
        var m = doc.RootElement.GetProperty("data");
        var modId = Num(m, "id");

        var canMatch = mc != null && hit.Kind != ModKind.Modpack && (hit.Kind == ModKind.ResourcePack || loader != null);
        JsonElement? file = null;
        var matched = false;
        if (canMatch)
        {
            file = await PickCurseFileAsync(modId, mc, hit.Kind == ModKind.Mod ? CurseLoader(loader!) : 0, ct);
            matched = file != null;
        }
        file ??= await PickCurseFileAsync(modId, null, 0, ct);

        var changelog = "";
        if (file is { } f0)
        {
            try
            {
                using var cl = await GetJsonAsync($"{CurseApi}/mods/{modId}/files/{Num(f0, "id")}/changelog", true, ct);
                changelog = CleanText(Str(cl.RootElement, "data"), 900);
            }
            catch (ModException) { /* changelog là phần phụ, bỏ qua nếu lỗi */ }
        }

        var d = new ModDetail
        {
            Title = Str(m, "name"),
            Description = Str(m, "summary"),
            PageUrl = hit.PageUrl,
            Changelog = changelog
        };

        if (file is { } f)
        {
            var dn = Str(f, "displayName");
            d.Rows.Add(new ModDetailRow(matched ? "Phiên bản phù hợp" : "Phiên bản mới nhất", dn.Length > 0 ? dn : Str(f, "fileName")));
            if (FormatDate(f, "fileDate") is { Length: > 0 } fd) d.Rows.Add(new ModDetailRow("Ngày phát hành bản này", fd));
            d.Rows.Add(new ModDetailRow("Tệp tải", $"{Str(f, "fileName")} ({FormatSize(Num(f, "fileLength"))})"));
            var fMc = McVersions.Range(Strings(f, "gameVersions"));
            if (fMc.Length > 0) d.Rows.Add(new ModDetailRow("Bản này cho Minecraft", fMc));
        }

        if (FormatDate(m, "dateModified") is { Length: > 0 } up) d.Rows.Add(new ModDetailRow("Cập nhật dự án", up));

        // gameVersions trong latestFiles trộn lẫn phiên bản Minecraft và tên loader
        var allGv = new List<string>();
        if (m.TryGetProperty("latestFiles", out var lf) && lf.ValueKind == JsonValueKind.Array)
            foreach (var x in lf.EnumerateArray()) allGv.AddRange(Strings(x, "gameVersions"));
        var sorted = McVersions.SortedReleases(allGv);
        if (sorted.Count > 0)
        {
            var text = string.Join(", ", sorted);
            d.Rows.Add(new ModDetailRow("Hỗ trợ Minecraft", text.Length > 240 ? text[..240] + "…" : text));
        }
        var ld = ModHit.OnlyLoaders(allGv).Select(LoaderInstaller.Label).Distinct().ToList();
        if (ld.Count > 0 && hit.Kind != ModKind.ResourcePack) d.Rows.Add(new ModDetailRow("Mod loader", string.Join(", ", ld)));

        d.Rows.Add(new ModDetailRow("Lượt tải", Num(m, "downloadCount").ToString("N0", Vi)));
        if (m.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Array)
        {
            var names = cats.EnumerateArray().Select(c => Str(c, "name")).Where(n => n.Length > 0).ToList();
            if (names.Count > 0) d.Rows.Add(new ModDetailRow("Thể loại", string.Join(", ", names)));
        }

        if (canMatch) d.Rows.Add(CompatRow(hit.Kind, mc!, loader, matched));
        return d;
    }

    // ================= Cài mod / resource pack =================

    private sealed class InstallCtx
    {
        public string Mc = "";
        public string Loader = "";
        public ModKind Kind;
        public string TargetDir = "";
        public ModIndex Index = new();
        public ModInstallResult Result = new();
        public HashSet<string> Visited = new();
        public Action<double, string> Report = (_, _) => { };
        public string? PinnedId;   // id phiên bản (Modrinth) hoặc id file (CurseForge) người dùng đã chọn
    }

    /// <summary>
    /// Cài một mod hoặc resource pack vào thư mục game của instance, đúng phiên bản Minecraft
    /// (và loader với mod). Mod được cài kèm các mod phụ thuộc bắt buộc.
    /// </summary>
    public async Task<ModInstallResult> InstallAsync(ModHit hit, string mc, string loader, string gameDir,
        Action<double, string> report, CancellationToken ct, string? versionId = null)
    {
        if (hit.Kind == ModKind.Modpack)
            throw new ModException("Modpack được nhập thành một instance mới, không cài vào instance có sẵn.");
        if (hit.Kind == ModKind.Mod && CurseLoader(loader) == 0)
            throw new ModException("Instance chưa có mod loader. Hãy cài loader ở tab Instance trước.");

        var targetDir = Path.Combine(gameDir, hit.Kind == ModKind.Mod ? "mods" : "resourcepacks");
        Directory.CreateDirectory(targetDir);

        var ctx = new InstallCtx
        {
            Mc = mc, Loader = loader, Kind = hit.Kind, TargetDir = targetDir,
            Index = ModIndex.Load(gameDir), Report = report, PinnedId = versionId
        };

        if (hit.Source == "CurseForge")
        {
            RequireCurseKey();
            await InstallCurseAsync(long.Parse(hit.Id), hit.Title, ctx, 0, ct);
        }
        else
        {
            await InstallModrinthAsync(hit.Id, hit.Title, ctx, 0, ct);
        }

        ctx.Index.Save(gameDir);
        return ctx.Result;
    }

    // ---------- Modrinth ----------

    private async Task InstallModrinthAsync(string projectId, string title, InstallCtx ctx, int depth, CancellationToken ct)
    {
        var key = "modrinth:" + projectId;
        if (!ctx.Visited.Add(key)) return;
        if (depth > 0 && ctx.Index.Has(key, ctx.TargetDir)) return; // mod phụ thuộc đã có sẵn

        JsonElement? ver;
        if (depth == 0 && ctx.PinnedId != null)
        {
            // Người dùng đã chọn đúng phiên bản: lấy chính phiên bản đó, không tự chọn bản khác
            ctx.Report(-1, $"Đang lấy phiên bản đã chọn của {title}...");
            using var vd = await GetJsonAsync($"{ModrinthApi}/version/{Uri.EscapeDataString(ctx.PinnedId)}", false, ct);
            ver = vd.RootElement.Clone();
        }
        else
        {
            ctx.Report(-1, $"Đang tìm bản phù hợp của {title}...");
            var loaders = ctx.Kind == ModKind.Mod ? ModrinthLoaders(ctx.Loader) : new[] { "minecraft" };
            ver = await PickModrinthVersionAsync(projectId, ctx.Mc, loaders, ct);
            // Resource pack đôi khi không gắn loader "minecraft": thử lại chỉ theo phiên bản
            if (ver == null && ctx.Kind == ModKind.ResourcePack)
                ver = await PickModrinthVersionAsync(projectId, ctx.Mc, null, ct);
        }

        if (ver == null)
        {
            var need = ctx.Kind == ModKind.Mod ? $"Minecraft {ctx.Mc} + {LoaderInstaller.Label(ctx.Loader)}" : $"Minecraft {ctx.Mc}";
            var msg = $"{title}: không có bản cho {need}.";
            if (depth == 0) throw new ModException(msg);
            ctx.Result.Warnings.Add(msg);
            return;
        }
        var v = ver.Value;

        var file = PrimaryFile(v) ?? throw new ModException($"{title}: phiên bản này không có file tải.");
        var fname = Path.GetFileName(Str(file, "filename"));
        string? sha1 = null, sha512 = null;
        if (file.TryGetProperty("hashes", out var hs) && hs.ValueKind == JsonValueKind.Object)
        {
            sha1 = Str(hs, "sha1") is { Length: > 0 } a ? a : null;
            sha512 = Str(hs, "sha512") is { Length: > 0 } b ? b : null;
        }

        ctx.Report(-1, $"Đang tải {fname}...");
        await DownloadIntoAsync(key, fname, new[] { Str(file, "url") }, sha1, sha512, ctx, ct);

        // Mod phụ thuộc bắt buộc (resource pack không có)
        if (ctx.Kind == ModKind.Mod && depth < MaxDepth &&
            v.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in deps.EnumerateArray())
            {
                if (Str(d, "dependency_type") != "required") continue;
                var depId = Str(d, "project_id");
                if (depId.Length == 0) continue;
                await InstallModrinthAsync(depId, "mod phụ thuộc " + depId, ctx, depth + 1, ct);
            }
        }
    }

    /// <summary>File chính của một phiên bản Modrinth (ưu tiên file đánh dấu primary).</summary>
    private static JsonElement? PrimaryFile(JsonElement version)
    {
        JsonElement? first = null;
        if (version.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in files.EnumerateArray())
            {
                first ??= f.Clone();
                if (f.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True) return f.Clone();
            }
        }
        return first;
    }

    /// <summary>Lấy phiên bản mới nhất, ưu tiên release rồi beta rồi alpha. mc/loaders null thì không lọc.</summary>
    private async Task<JsonElement?> PickModrinthVersionAsync(string projectId, string? mc, string[]? loaders, CancellationToken ct)
    {
        var url = $"{ModrinthApi}/project/{Uri.EscapeDataString(projectId)}/version";
        var q = new List<string>();
        if (mc != null) q.Add("game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { mc })));
        if (loaders != null) q.Add("loaders=" + Uri.EscapeDataString(JsonSerializer.Serialize(loaders)));
        if (q.Count > 0) url += "?" + string.Join("&", q);

        using var doc = await GetJsonAsync(url, false, ct);

        JsonElement? best = null;
        var bestRank = 99;
        foreach (var v in doc.RootElement.EnumerateArray())
        {
            var rank = Str(v, "version_type") switch { "release" => 0, "beta" => 1, _ => 2 };
            if (rank >= bestRank) continue;
            best = v.Clone();
            bestRank = rank;
            if (rank == 0) break;
        }
        return best;
    }

    // ---------- CurseForge ----------

    private async Task InstallCurseAsync(long modId, string title, InstallCtx ctx, int depth, CancellationToken ct)
    {
        var key = "curseforge:" + modId;
        if (!ctx.Visited.Add(key)) return;
        if (depth > 0 && ctx.Index.Has(key, ctx.TargetDir)) return;

        JsonElement? file;
        if (depth == 0 && ctx.PinnedId != null)
        {
            ctx.Report(-1, $"Đang lấy phiên bản đã chọn của {title}...");
            using var fd = await GetJsonAsync($"{CurseApi}/mods/{modId}/files/{Uri.EscapeDataString(ctx.PinnedId)}", true, ct);
            file = fd.RootElement.GetProperty("data").Clone();
        }
        else
        {
            ctx.Report(-1, $"Đang tìm bản phù hợp của {title}...");
            file = await PickCurseFileAsync(modId, ctx.Mc, ctx.Kind == ModKind.Mod ? CurseLoader(ctx.Loader) : 0, ct);
        }
        if (file == null)
        {
            var need = ctx.Kind == ModKind.Mod ? $"Minecraft {ctx.Mc} + {LoaderInstaller.Label(ctx.Loader)}" : $"Minecraft {ctx.Mc}";
            var msg = $"{title}: không có bản cho {need}.";
            if (depth == 0) throw new ModException(msg);
            ctx.Result.Warnings.Add(msg);
            return;
        }
        var f = file.Value;

        var (fname, url, sha1) = CurseFileInfo(f);

        ctx.Report(-1, $"Đang tải {fname}...");
        try
        {
            await DownloadIntoAsync(key, fname, new[] { url }, sha1, null, ctx, ct);
        }
        catch (IOException ex) when (depth == 0)
        {
            throw new ModException($"Không tải được {fname}. Tác giả có thể đã tắt tải qua launcher bên thứ ba; hãy tải thủ công trên trang CurseForge.", ex);
        }

        if (ctx.Kind == ModKind.Mod && depth < MaxDepth &&
            f.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in deps.EnumerateArray())
            {
                if (Num(d, "relationType") != 3) continue; // 3 = phụ thuộc bắt buộc
                var depId = Num(d, "modId");
                if (depId == 0) continue;
                await InstallCurseAsync(depId, "mod phụ thuộc " + depId, ctx, depth + 1, ct);
            }
        }
    }

    /// <summary>Tên file, link tải (tự suy ra từ CDN nếu tác giả không công khai link) và SHA-1 của một file CurseForge.</summary>
    private static (string Name, string Url, string? Sha1) CurseFileInfo(JsonElement f)
    {
        var id = Num(f, "id");
        var name = Path.GetFileName(Str(f, "fileName"));
        var url = Str(f, "downloadUrl");
        if (url.Length == 0)
            url = $"https://edge.forgecdn.net/files/{id / 1000}/{id % 1000}/{Uri.EscapeDataString(name)}";

        string? sha1 = null;
        if (f.TryGetProperty("hashes", out var hs) && hs.ValueKind == JsonValueKind.Array)
        {
            foreach (var h in hs.EnumerateArray())
                if (h.TryGetProperty("algo", out var al) && al.TryGetInt32(out var a) && a == 1) sha1 = Str(h, "value");
        }
        return (name, url, string.IsNullOrEmpty(sha1) ? null : sha1);
    }

    /// <summary>Chọn file mới nhất, ưu tiên release. mc null thì không lọc phiên bản; cfLoader 0 thì không lọc loader.</summary>
    private async Task<JsonElement?> PickCurseFileAsync(long modId, string? mc, int cfLoader, CancellationToken ct)
    {
        var url = $"{CurseApi}/mods/{modId}/files?pageSize=30";
        if (mc != null) url += "&gameVersion=" + Uri.EscapeDataString(mc);
        if (cfLoader != 0) url += "&modLoaderType=" + cfLoader;
        using var doc = await GetJsonAsync(url, true, ct);

        JsonElement? best = null;
        var bestRank = 99;
        var bestDate = "";
        foreach (var f in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            if (f.TryGetProperty("isAvailable", out var av) && av.ValueKind == JsonValueKind.False) continue;
            var rank = Num(f, "releaseType") switch { 1 => 0, 2 => 1, _ => 2 };
            var date = Str(f, "fileDate");
            if (rank < bestRank || (rank == bestRank && string.CompareOrdinal(date, bestDate) > 0))
            {
                best = f.Clone();
                bestRank = rank;
                bestDate = date;
            }
        }
        return best;
    }

    // ================= Modpack: tải file về để nhập thành instance =================

    /// <summary>Tải file modpack (.mrpack hoặc .zip CurseForge) bản mới nhất vào thư mục cache và trả về đường dẫn.</summary>
    public async Task<string> DownloadModpackAsync(ModHit hit, string cacheDir, Action<double, string> report, CancellationToken ct,
        string? versionId = null)
    {
        Directory.CreateDirectory(cacheDir);
        report(-1, $"Đang tìm bản mới nhất của {hit.Title}...");

        if (hit.Source == "CurseForge")
        {
            RequireCurseKey();
            JsonElement? picked;
            if (versionId != null)
            {
                using var fd = await GetJsonAsync($"{CurseApi}/mods/{hit.Id}/files/{Uri.EscapeDataString(versionId)}", true, ct);
                picked = fd.RootElement.GetProperty("data").Clone();
            }
            else
            {
                picked = await PickCurseFileAsync(long.Parse(hit.Id), null, 0, ct);
            }
            var file = picked ?? throw new ModException($"{hit.Title}: modpack này chưa có bản để tải.");
            var (name, url, sha1) = CurseFileInfo(file);
            var dest = Path.Combine(cacheDir, name);
            report(-1, $"Đang tải {name}...");
            try { await DownloadFileAsync(new[] { url }, dest, sha1, null, ct); }
            catch (IOException ex)
            {
                throw new ModException($"Không tải được {name}. Tác giả có thể đã tắt tải qua launcher bên thứ ba; hãy tải thủ công trên trang CurseForge rồi dùng nút \"Nhập modpack\".", ex);
            }
            return dest;
        }

        JsonElement? verPicked;
        if (versionId != null)
        {
            using var vd = await GetJsonAsync($"{ModrinthApi}/version/{Uri.EscapeDataString(versionId)}", false, ct);
            verPicked = vd.RootElement.Clone();
        }
        else
        {
            verPicked = await PickModrinthVersionAsync(hit.Id, null, null, ct);
        }
        var ver = verPicked ?? throw new ModException($"{hit.Title}: modpack này chưa có bản để tải.");
        var f = PrimaryFile(ver) ?? throw new ModException($"{hit.Title}: phiên bản này không có file tải.");
        var fname = Path.GetFileName(Str(f, "filename"));
        string? s1 = null, s512 = null;
        if (f.TryGetProperty("hashes", out var hs) && hs.ValueKind == JsonValueKind.Object)
        {
            s1 = Str(hs, "sha1") is { Length: > 0 } a ? a : null;
            s512 = Str(hs, "sha512") is { Length: > 0 } b ? b : null;
        }

        var target = Path.Combine(cacheDir, fname);
        report(-1, $"Đang tải {fname}...");
        await DownloadFileAsync(new[] { Str(f, "url") }, target, s1, s512, ct);
        return target;
    }

    // ================= Tải file =================

    private async Task DownloadIntoAsync(string key, string fname, string[] urls, string? sha1, string? sha512,
        InstallCtx ctx, CancellationToken ct)
    {
        if (fname.Length == 0) throw new ModException("Tên file không hợp lệ.");
        var dest = Path.Combine(ctx.TargetDir, fname);

        // Gỡ bản cũ của cùng một mục (khác tên file) để không bị trùng
        if (ctx.Index.Files.TryGetValue(key, out var old) && !old.Equals(fname, StringComparison.OrdinalIgnoreCase))
        {
            var oldPath = Path.Combine(ctx.TargetDir, old);
            try { if (File.Exists(oldPath)) { File.Delete(oldPath); _log($"Đã gỡ bản cũ {old}."); } } catch { }
        }

        if (!(File.Exists(dest) && VerifyHash(dest, sha1, sha512)))
            await DownloadFileAsync(urls, dest, sha1, sha512, ct);

        ctx.Index.Files[key] = fname;
        ctx.Result.Installed.Add(fname);
        _log($"Đã cài {fname}");
    }

    private async Task DownloadFileAsync(string[] urls, string dest, string? sha1, string? sha512, CancellationToken ct)
    {
        var tmp = dest + ".tmp";
        Exception? last = null;

        foreach (var url in urls)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !AllowedHosts.Contains(uri.Host))
            {
                last = new IOException("Link tải không nằm trong danh sách máy chủ cho phép: " + url);
                continue;
            }

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using (var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
                    {
                        resp.EnsureSuccessStatusCode();
                        await using var src = await resp.Content.ReadAsStreamAsync(ct);
                        await using var fs = File.Create(tmp);
                        await src.CopyToAsync(fs, ct);
                    }
                    if (!VerifyHash(tmp, sha1, sha512)) throw new InvalidDataException("sai mã băm");
                    File.Move(tmp, dest, true);
                    return;
                }
                catch (OperationCanceledException) { try { File.Delete(tmp); } catch { } throw; }
                catch (Exception ex)
                {
                    last = ex;
                    try { File.Delete(tmp); } catch { }
                    if (attempt < 3) await Task.Delay(700 * attempt, ct);
                }
            }
        }
        throw new IOException($"Không tải được {Path.GetFileName(dest)}: {last?.Message}", last);
    }

    private static bool VerifyHash(string path, string? sha1, string? sha512)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (sha512 != null) return Convert.ToHexString(SHA512.HashData(fs)).Equals(sha512, StringComparison.OrdinalIgnoreCase);
            if (sha1 != null) return Convert.ToHexString(SHA1.HashData(fs)).Equals(sha1, StringComparison.OrdinalIgnoreCase);
            return true;
        }
        catch { return false; }
    }
}

/// <summary>
/// Sổ ghi mục đã cài qua launcher (mods-index.json nằm trong thư mục game của instance): nhớ mục nào ứng với file nào
/// để khi cài lại bản mới thì gỡ bản cũ, và để bỏ qua mod phụ thuộc đã có.
/// </summary>
internal sealed class ModIndex
{
    public Dictionary<string, string> Files { get; set; } = new();

    private static string PathFor(string gameDir) => Path.Combine(gameDir, "mods-index.json");

    public static ModIndex Load(string gameDir)
    {
        try
        {
            var p = PathFor(gameDir);
            if (File.Exists(p))
                return JsonSerializer.Deserialize<ModIndex>(File.ReadAllText(p)) ?? new ModIndex();
        }
        catch { /* sổ hỏng thì bắt đầu lại */ }
        return new ModIndex();
    }

    public void Save(string gameDir)
    {
        try { File.WriteAllText(PathFor(gameDir), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* không ghi được cũng không sao */ }
    }

    public bool Has(string key, string targetDir) =>
        Files.TryGetValue(key, out var f) && File.Exists(Path.Combine(targetDir, f));
}
