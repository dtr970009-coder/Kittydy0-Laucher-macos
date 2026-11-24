using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

namespace KittyDy0Launcher;

/// <summary>
/// Cài mod loader vào thư mục dùng chung (shared) và trả về id phiên bản để CmlLib chạy.
/// - Fabric / Quilt: tải file profile JSON từ API meta chính thức (không cần Java).
/// - Forge / NeoForge: tải file installer rồi chạy "java -jar installer.jar --installClient".
/// </summary>
public class LoaderInstaller
{
    private static readonly HttpClient Http = CreateClient();
    private readonly Action<string> _log;

    public LoaderInstaller(Action<string> log) => _log = log;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KittyDy0Launcher/1.0");
        return c;
    }

    public static string Label(string type) => type.ToLowerInvariant() switch
    {
        "fabric" => "Fabric",
        "quilt" => "Quilt",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        _ => type
    };

    private static string VersionsDir => Path.Combine(AppPaths.Shared, "versions");

    /// <summary>Cài loader. <paramref name="loaderVersion"/> để trống thì tự chọn bản mới nhất.</summary>
    /// <param name="javaProvider">Chỉ được gọi với Forge/NeoForge: trả về đường dẫn java (java.exe trên Windows) để chạy installer.</param>
    public async Task<string> InstallAsync(string type, string mcVersion, string loaderVersion,
        Func<CancellationToken, Task<string>> javaProvider, Action<double, string> report, CancellationToken ct)
    {
        type = type.ToLowerInvariant();
        switch (type)
        {
            case "fabric":
            case "quilt":
                return await InstallFabricLikeAsync(type, mcVersion, loaderVersion, report, ct);
            case "forge":
            case "neoforge":
                return await InstallForgeLikeAsync(type, mcVersion, loaderVersion, javaProvider, report, ct);
            default:
                throw new NotSupportedException($"Chưa hỗ trợ mod loader \"{type}\".");
        }
    }

    // ---------- Fabric / Quilt ----------

    private async Task<string> InstallFabricLikeAsync(string type, string mc, string ver,
        Action<double, string> report, CancellationToken ct)
    {
        var baseUrl = type == "fabric" ? "https://meta.fabricmc.net/v2" : "https://meta.quiltmc.org/v3";

        if (string.IsNullOrWhiteSpace(ver))
        {
            report(-1, $"Đang tìm bản {Label(type)} mới nhất cho {mc}...");
            var listJson = await Http.GetStringAsync($"{baseUrl}/versions/loader/{Uri.EscapeDataString(mc)}", ct);
            using var doc = JsonDocument.Parse(listJson);

            string? first = null, stableVer = null;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var loader = item.GetProperty("loader");
                var v = loader.GetProperty("version").GetString();
                if (string.IsNullOrEmpty(v)) continue;
                first ??= v;
                // Fabric có cờ "stable"; Quilt thì coi bản không có dấu '-' là ổn định
                var stable = loader.TryGetProperty("stable", out var st)
                    ? st.ValueKind == JsonValueKind.True
                    : !v.Contains('-');
                if (stable) { stableVer = v; break; }
            }
            ver = stableVer ?? first ?? throw new InvalidOperationException(
                $"{Label(type)} chưa hỗ trợ Minecraft {mc}.");
        }

        // Id mặc định của profile: fabric-loader-<loader>-<mc>
        var expectedId = $"{type}-loader-{ver}-{mc}";
        if (File.Exists(Path.Combine(VersionsDir, expectedId, expectedId + ".json")))
        {
            _log($"{Label(type)} {ver} đã có sẵn.");
            return expectedId;
        }

        report(-1, $"Đang tải {Label(type)} {ver}...");
        var url = $"{baseUrl}/versions/loader/{Uri.EscapeDataString(mc)}/{Uri.EscapeDataString(ver)}/profile/json";
        var json = await Http.GetStringAsync(url, ct);

        string id;
        using (var doc = JsonDocument.Parse(json))
        {
            id = doc.RootElement.GetProperty("id").GetString() ?? expectedId;
        }
        // Id nằm trong tên thư mục nên phải kiểm tra kỹ
        if (!Regex.IsMatch(id, @"^[A-Za-z0-9._+\-]+$"))
            throw new InvalidDataException("Id phiên bản loader không hợp lệ: " + id);

        var dir = Path.Combine(VersionsDir, id);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, id + ".json"), json, ct);
        _log($"Đã cài {Label(type)} {ver} (id {id}).");
        return id;
    }

    // ---------- Forge / NeoForge ----------

    private async Task<string> InstallForgeLikeAsync(string type, string mc, string ver,
        Func<CancellationToken, Task<string>> javaProvider, Action<double, string> report, CancellationToken ct)
    {
        var neo = type == "neoforge";

        if (string.IsNullOrWhiteSpace(ver))
        {
            report(-1, $"Đang tìm bản {Label(type)} mới nhất cho {mc}...");
            ver = await FindLatestForgeLikeAsync(neo, mc, ct);
        }

        var existing = FindInstalledId(type, ver);
        if (existing != null)
        {
            _log($"{Label(type)} {ver} đã có sẵn (id {existing}).");
            return existing;
        }

        // Installer cần bản Minecraft gốc có sẵn trong thư mục dùng chung
        await EnsureVanillaAsync(mc, Label(type), report, ct);

        // Tải file installer
        report(-1, $"Đang tải trình cài {Label(type)} {ver}...");
        string[] urls = neo
            ? new[] { $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{ver}/neoforge-{ver}-installer.jar" }
            : new[]
            {
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mc}-{ver}/forge-{mc}-{ver}-installer.jar",
                // Một số bản cũ có thêm hậu tố phiên bản Minecraft
                $"https://maven.minecraftforge.net/net/minecraftforge/forge/{mc}-{ver}-{mc}/forge-{mc}-{ver}-{mc}-installer.jar"
            };
        var jar = await DownloadFirstAsync(urls, Path.Combine(AppPaths.Cache, "loader"), ct)
                  ?? throw new IOException($"Không tải được trình cài {Label(type)} {ver}. Kiểm tra mạng hoặc số phiên bản.");

        var java = await javaProvider(ct);
        EnsureLauncherProfiles();

        var before = ListVersionIds();
        report(-1, $"Đang chạy trình cài {Label(type)} (có thể mất vài phút)...");
        await RunInstallerAsync(java, jar, ct);
        var after = ListVersionIds();

        var id = after.Except(before).FirstOrDefault(n => MatchesType(n, type))
                 ?? FindInstalledId(type, ver)
                 ?? throw new InvalidOperationException(
                     $"Trình cài chạy xong nhưng không thấy phiên bản {Label(type)} mới. Xem tab Nhật ký.");
        _log($"Đã cài {Label(type)} {ver} (id {id}).");
        return id;
    }

    private async Task<string> FindLatestForgeLikeAsync(bool neo, string mc, CancellationToken ct)
    {
        if (!neo)
        {
            var json = await Http.GetStringAsync("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", ct);
            using var doc = JsonDocument.Parse(json);
            var promos = doc.RootElement.GetProperty("promos");
            foreach (var key in new[] { $"{mc}-recommended", $"{mc}-latest" })
            {
                if (promos.TryGetProperty(key, out var v) && v.GetString() is { Length: > 0 } s) return s;
            }
            throw new InvalidOperationException($"Forge chưa hỗ trợ Minecraft {mc}.");
        }
        else
        {
            // NeoForge đánh số theo Minecraft: 1.21.1 -> 21.1.x, 1.21 -> 21.0.x
            var m = Regex.Match(mc, @"^1\.(\d+)(?:\.(\d+))?");
            if (!m.Success) throw new InvalidOperationException($"Không xác định được bản NeoForge cho Minecraft {mc}.");
            var prefix = $"{m.Groups[1].Value}.{(m.Groups[2].Success ? m.Groups[2].Value : "0")}.";

            var json = await Http.GetStringAsync("https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", ct);
            using var doc = JsonDocument.Parse(json);
            var best = doc.RootElement.GetProperty("versions").EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => s.StartsWith(prefix) && !s.Contains('-') && Version.TryParse(s, out _))
                .OrderBy(s => Version.Parse(s))
                .LastOrDefault();
            return best ?? throw new InvalidOperationException($"NeoForge chưa hỗ trợ Minecraft {mc}.");
        }
    }

    /// <summary>Tải bản Minecraft gốc vào thư mục dùng chung bằng đúng API CmlLib mà GameRunner đang dùng.</summary>
    private static async Task EnsureVanillaAsync(string mc, string loaderName, Action<double, string> report, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.Shared);
        var launcher = new MinecraftLauncher(new MinecraftPath(AppPaths.Shared));
        launcher.FileProgressChanged += (_, e) =>
        {
            var pct = e.TotalTasks > 0 ? (double)e.ProgressedTasks / e.TotalTasks * 100 : 0;
            report(pct, $"Đang tải Minecraft {mc} cho {loaderName}: {e.Name} ({e.ProgressedTasks}/{e.TotalTasks})");
        };

        report(0, $"Đang tải Minecraft {mc} (cần cho {loaderName})...");
        var option = new MLaunchOption
        {
            Session = MSession.CreateOfflineSession("Player"),
            MaximumRamMb = 2048
        };
        // Chỉ cần phần "tải"; tiến trình trả về không được khởi động nên bỏ đi
        using var unused = await launcher.InstallAndBuildProcessAsync(mc, option);
        ct.ThrowIfCancellationRequested();
    }

    private async Task RunInstallerAsync(string java, string installerJar, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = java,
            WorkingDirectory = AppPaths.Cache,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installerJar);
        psi.ArgumentList.Add("--installClient");
        psi.ArgumentList.Add(AppPaths.Shared);

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) _log("[installer] " + e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) _log("[installer] " + e.Data); };

        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        // Hủy thì tắt luôn installer
        using var reg = ct.Register(() =>
        {
            try { if (!p.HasExited) p.Kill(true); } catch { }
        });

        await p.WaitForExitAsync(ct);
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"Trình cài loader thoát với mã {p.ExitCode}. Xem tab Nhật ký.");
    }

    /// <summary>Installer của Forge/NeoForge đòi có launcher_profiles.json trong thư mục đích.</summary>
    private static void EnsureLauncherProfiles()
    {
        var f = Path.Combine(AppPaths.Shared, "launcher_profiles.json");
        if (!File.Exists(f))
            File.WriteAllText(f, "{\"profiles\":{},\"selectedProfile\":\"\",\"version\":3}");
    }

    // ---------- Danh sách phiên bản loader ----------

    /// <summary>
    /// Liệt kê các bản mod loader có cho một phiên bản Minecraft, mới nhất đứng đầu.
    /// Ném lỗi nếu mất mạng; trả về danh sách rỗng nếu loader chưa hỗ trợ bản Minecraft đó.
    /// </summary>
    public async Task<List<string>> ListVersionsAsync(string type, string mc, CancellationToken ct)
    {
        type = type.ToLowerInvariant();
        var found = new List<string>();

        switch (type)
        {
            case "fabric":
            case "quilt":
            {
                var baseUrl = type == "fabric" ? "https://meta.fabricmc.net/v2" : "https://meta.quiltmc.org/v3";
                var json = await Http.GetStringAsync($"{baseUrl}/versions/loader/{Uri.EscapeDataString(mc)}", ct);
                using var doc = JsonDocument.Parse(json);
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    if (item.TryGetProperty("loader", out var l) && l.TryGetProperty("version", out var v) &&
                        v.GetString() is { Length: > 0 } s) found.Add(s);
                }
                break;
            }

            case "forge":
            {
                try
                {
                    var json = await Http.GetStringAsync("https://files.minecraftforge.net/net/minecraftforge/forge/maven-metadata.json", ct);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty(mc, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in arr.EnumerateArray())
                        {
                            var s = e.GetString() ?? "";
                            // "1.20.1-47.2.0" -> "47.2.0"; bản cũ có hậu tố "-1.7.10" thì cắt đi
                            if (s.StartsWith(mc + "-", StringComparison.Ordinal)) s = s[(mc.Length + 1)..];
                            var dash = s.IndexOf('-');
                            if (dash > 0) s = s[..dash];
                            if (s.Length > 0) found.Add(s);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Danh sách đầy đủ lỗi thì rơi về hai bản được Forge đánh dấu
                    _log("Không đọc được danh sách Forge đầy đủ (" + ex.Message + "), dùng bản recommended/latest.");
                    var json = await Http.GetStringAsync("https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", ct);
                    using var doc = JsonDocument.Parse(json);
                    var promos = doc.RootElement.GetProperty("promos");
                    foreach (var key in new[] { $"{mc}-recommended", $"{mc}-latest" })
                        if (promos.TryGetProperty(key, out var p) && p.GetString() is { Length: > 0 } s) found.Add(s);
                }
                break;
            }

            case "neoforge":
            {
                var m = Regex.Match(mc, @"^1\.(\d+)(?:\.(\d+))?");
                if (!m.Success) break; // NeoForge chỉ có cho Minecraft 1.x
                var prefix = $"{m.Groups[1].Value}.{(m.Groups[2].Success ? m.Groups[2].Value : "0")}.";

                var json = await Http.GetStringAsync("https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", ct);
                using var doc = JsonDocument.Parse(json);
                foreach (var e in doc.RootElement.GetProperty("versions").EnumerateArray())
                {
                    var s = e.GetString() ?? "";
                    if (s.StartsWith(prefix, StringComparison.Ordinal)) found.Add(s);
                }
                break;
            }

            default:
                throw new NotSupportedException($"Chưa hỗ trợ mod loader \"{type}\".");
        }

        // Mới nhất lên đầu; bản không đọc được số (beta, pre) xuống cuối
        return found
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderByDescending(s => Version.TryParse(s, out var v) ? v : new Version(0, 0))
            .ToList();
    }

    /// <summary>
    /// Đoán số phiên bản loader từ id phiên bản đã cài, ví dụ "fabric-loader-0.16.9-1.21.1" -> "0.16.9",
    /// "1.20.1-forge-47.2.0" -> "47.2.0", "neoforge-21.1.77" -> "21.1.77". Không đoán được thì trả về rỗng.
    /// </summary>
    public static string VersionFromId(string type, string id, string mc)
    {
        type = type.ToLowerInvariant();
        if (type is "fabric" or "quilt")
        {
            var prefix = $"{type}-loader-";
            if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return "";
            var rest = id[prefix.Length..];
            var suffix = "-" + mc;
            return rest.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? rest[..^suffix.Length] : rest;
        }

        var i = id.IndexOf(type + "-", StringComparison.OrdinalIgnoreCase);
        return i >= 0 ? id[(i + type.Length + 1)..] : "";
    }

    // ---------- Tiện ích ----------

    private static bool MatchesType(string id, string type) =>
        id.Contains(type, StringComparison.OrdinalIgnoreCase) &&
        (type != "forge" || !id.Contains("neoforge", StringComparison.OrdinalIgnoreCase));

    private static HashSet<string> ListVersionIds()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(VersionsDir)) return set;
        foreach (var d in Directory.GetDirectories(VersionsDir))
        {
            var n = Path.GetFileName(d);
            if (File.Exists(Path.Combine(d, n + ".json"))) set.Add(n);
        }
        return set;
    }

    private static string? FindInstalledId(string type, string ver) =>
        ListVersionIds().FirstOrDefault(n =>
            MatchesType(n, type) && n.Contains(ver, StringComparison.OrdinalIgnoreCase));

    /// <summary>Thử lần lượt các URL, trả về đường dẫn file đã tải (hoặc đã có sẵn trong cache).</summary>
    private async Task<string?> DownloadFirstAsync(string[] urls, string dir, CancellationToken ct)
    {
        Directory.CreateDirectory(dir);
        foreach (var url in urls)
        {
            var dest = Path.Combine(dir, Path.GetFileName(new Uri(url).AbsolutePath));
            if (File.Exists(dest) && new FileInfo(dest).Length > 0) return dest;

            var tmp = dest + ".tmp";
            try
            {
                using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        _log($"Không tải được {url} (HTTP {(int)resp.StatusCode}), thử nguồn khác.");
                        continue;
                    }
                    await using var src = await resp.Content.ReadAsStreamAsync(ct);
                    await using var fs = File.Create(tmp);
                    await src.CopyToAsync(fs, ct);
                }
                File.Move(tmp, dest, true);
                return dest;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log($"Lỗi tải {url}: {ex.Message}");
                try { File.Delete(tmp); } catch { }
            }
        }
        return null;
    }
}
