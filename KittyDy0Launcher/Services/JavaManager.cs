using System.Diagnostics;
using System.Net.Http;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KittyDy0Launcher;

/// <summary>
/// Tìm và tự tải Java từ nhiều nguồn: Adoptium Temurin, Azul Zulu, Microsoft OpenJDK
/// (Mojang do CmlLib tự xử lý). Java do launcher tải nằm trong thư mục "java" của AppPaths.Root
/// (Windows: zip; macOS/Linux: tar.gz).
/// </summary>
public class JavaManager
{
    private static readonly HttpClient Http = CreateClient();
    private readonly Action<string> _log;

    public JavaManager(Action<string> log) => _log = log;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("KittyDy0Launcher/1.0");
        return c;
    }

    /// <summary>Mojang ghi Java 16 cho 1.17, nhưng Java 17 chạy tốt và có sẵn ở mọi nguồn.</summary>
    public static int Normalize(int major) => major == 16 ? 17 : major;

    public static string? VendorOf(JavaSourceKind s) => s switch
    {
        JavaSourceKind.Adoptium => "adoptium",
        JavaSourceKind.Azul => "azul",
        JavaSourceKind.Microsoft => "microsoft",
        _ => null
    };

    private static string VendorLabel(string v) => v switch
    {
        "adoptium" => "Adoptium Temurin",
        "azul" => "Azul Zulu",
        "microsoft" => "Microsoft OpenJDK",
        _ => v
    };

    public static string ManagedDir(string vendor, int major) => Path.Combine(AppPaths.Java, $"{vendor}-{major}");

    /// <summary>Tìm bin/java (java.exe trên Windows) bên trong một thư mục Java đã giải nén.</summary>
    public static string? FindJavaExe(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        try
        {
            // Bỏ qua symlink (JRE trên macOS có vài liên kết) để không đi vòng; file thật vẫn được thấy
            var opts = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint
            };
            return Directory.EnumerateFiles(dir, Platform.JavaExeName, opts)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "bin",
                    StringComparison.OrdinalIgnoreCase));
        }
        catch { return null; }
    }

    public static string[] ListManaged()
    {
        if (!Directory.Exists(AppPaths.Java)) return Array.Empty<string>();
        return Directory.GetDirectories(AppPaths.Java)
            .Select(d => Path.GetFileName(d))
            .Where(n => !string.IsNullOrEmpty(n) && !n.EndsWith(".tmp"))
            .OrderBy(n => n)
            .ToArray();
    }

    /// <summary>
    /// Trả về đường dẫn java (java.exe) cần dùng. Trả về null nghĩa là để thư viện CmlLib dùng Java của Mojang.
    /// </summary>
    public async Task<string?> ResolveAsync(int major, JavaSourceKind source, string customPath,
        Action<double, string>? progress, CancellationToken ct)
    {
        major = Normalize(major);

        switch (source)
        {
            case JavaSourceKind.Custom:
                if (string.IsNullOrWhiteSpace(customPath) || !File.Exists(customPath))
                    throw new FileNotFoundException("Không tìm thấy file Java đã chọn: " + customPath);
                return customPath;

            case JavaSourceKind.Mojang:
                return null;

            case JavaSourceKind.System:
            {
                var p = await FindSystemJavaAsync(major, ct);
                return p ?? throw new InvalidOperationException($"Không tìm thấy Java {major} có sẵn trên máy.");
            }

            case JavaSourceKind.Adoptium:
            case JavaSourceKind.Azul:
            case JavaSourceKind.Microsoft:
            {
                var vendor = VendorOf(source)!;
                var existing = FindJavaExe(ManagedDir(vendor, major));
                if (existing != null) return existing;
                return await DownloadChainAsync(major, vendor, progress, ct);
            }

            default: // Auto (và Global đã được quy về Auto)
            {
                var managed = FindAnyManaged(major);
                if (managed != null) return managed;

                var sys = await FindSystemJavaAsync(major, ct);
                if (sys != null) return sys;

                var downloaded = await DownloadChainAsync(major, null, progress, ct);
                if (downloaded != null) return downloaded;

                _log("Mọi nguồn Java đều lỗi, chuyển sang Java của Mojang.");
                return null;
            }
        }
    }

    private static string? FindAnyManaged(int major)
    {
        if (!Directory.Exists(AppPaths.Java)) return null;
        foreach (var d in Directory.GetDirectories(AppPaths.Java, $"*-{major}"))
        {
            var exe = FindJavaExe(d);
            if (exe != null) return exe;
        }
        return null;
    }

    /// <summary>Thử lần lượt các nguồn tải; nguồn ưu tiên (nếu có) được thử trước.</summary>
    public async Task<string?> DownloadChainAsync(int major, string? firstVendor,
        Action<double, string>? progress, CancellationToken ct)
    {
        major = Normalize(major);
        var order = new List<string> { "adoptium", "azul", "microsoft" };
        if (firstVendor != null && order.Remove(firstVendor)) order.Insert(0, firstVendor);

        foreach (var vendor in order)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await EnsureManagedAsync(vendor, major, progress, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log($"Nguồn {VendorLabel(vendor)} lỗi: {ex.Message}");
            }
        }
        return null;
    }

    private async Task<string> EnsureManagedAsync(string vendor, int major,
        Action<double, string>? progress, CancellationToken ct)
    {
        var dir = ManagedDir(vendor, major);
        var exe = FindJavaExe(dir);
        if (exe != null) return exe;

        var label = VendorLabel(vendor);
        var url = await GetDownloadUrlAsync(vendor, major, ct);
        Directory.CreateDirectory(AppPaths.Java);
        Directory.CreateDirectory(AppPaths.Cache);
        var archive = Path.Combine(AppPaths.Cache, $"java-{vendor}-{major}{ArchiveExt}");

        _log($"Tải Java {major} từ {label}: {url}");
        progress?.Invoke(0, $"Đang tải Java {major} ({label})...");
        await DownloadFileAsync(url, archive,
            pct => progress?.Invoke(pct * 100, $"Đang tải Java {major} ({label})... {pct:P0}"), ct);

        progress?.Invoke(100, $"Đang giải nén Java {major}...");
        var tmp = dir + ".tmp";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        await ExtractAsync(archive, tmp, ct);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.Move(tmp, dir);
        try { File.Delete(archive); } catch { }

        exe = FindJavaExe(dir) ?? throw new FileNotFoundException($"Giải nén xong nhưng không thấy {Platform.JavaExeName}.");
        EnsureExecutable(exe);
        _log($"Đã cài Java {major} ({label}) tại {dir}");
        return exe;
    }

    private static string ArchiveExt => Platform.IsWindows ? ".zip" : ".tar.gz";

    private static async Task ExtractAsync(string archive, string dest, CancellationToken ct)
    {
        if (Platform.IsWindows)
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, dest), ct);
            return;
        }

        // tar.gz: .NET tự giữ quyền thực thi và symlink trên macOS/Linux, và chặn đường dẫn thoát khỏi thư mục đích
        await using var fs = File.OpenRead(archive);
        await using var gz = new GZipStream(fs, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gz, dest, overwriteFiles: true, ct);
    }

    /// <summary>Đảm bảo file java có quyền chạy (phòng khi gói tải về mất quyền thực thi).</summary>
    private static void EnsureExecutable(string exe)
    {
        if (Platform.IsWindows) return;
        try
        {
            var mode = File.GetUnixFileMode(exe);
            File.SetUnixFileMode(exe, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch { }
    }

    private static async Task<string> GetDownloadUrlAsync(string vendor, int major, CancellationToken ct)
    {
        switch (vendor)
        {
            case "adoptium":
                return $"https://api.adoptium.net/v3/binary/latest/{major}/ga/{Platform.AdoptiumOs}/{Platform.AdoptiumArch}/jre/hotspot/normal/eclipse";

            case "azul":
            {
                var os = Platform.IsWindows ? "windows" : Platform.IsMac ? "macos" : "linux";
                var arch = Platform.IsArm64 ? "arch=arm&hw_bitness=64" : "arch=x64";
                var ext = Platform.IsWindows ? "zip" : "tar.gz";
                var api = "https://api.azul.com/metadata/v1/zulu/packages/?java_version=" + major +
                          $"&os={os}&{arch}&archive_type={ext}&java_package_type=jre&javafx_bundled=false" +
                          "&latest=true&release_status=ga&availability_types=CA&page=1&page_size=1";
                var json = await Http.GetStringAsync(api, ct);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 &&
                    root[0].TryGetProperty("download_url", out var u) && u.GetString() is { Length: > 0 } s)
                    return s;
                throw new InvalidOperationException($"Azul không có bản Java {major} cho máy này.");
            }

            case "microsoft":
            {
                if (major < 11) throw new InvalidOperationException("Microsoft OpenJDK chỉ có từ Java 11 trở lên.");
                var os = Platform.IsWindows ? "windows" : Platform.IsMac ? "macos" : "linux";
                var ext = Platform.IsWindows ? "zip" : "tar.gz";
                return $"https://aka.ms/download-jdk/microsoft-jdk-{major}-{os}-{Platform.AdoptiumArch}.{ext}";
            }

            default:
                throw new InvalidOperationException("Nguồn Java không hợp lệ: " + vendor);
        }
    }

    private static async Task DownloadFileAsync(string url, string dest, Action<double>? onPercent, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        var buf = new byte[81920];
        long read = 0;
        var lastReport = 0.0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if (total > 0)
            {
                var pct = (double)read / total;
                if (pct - lastReport >= 0.01) { lastReport = pct; onPercent?.Invoke(pct); }
            }
        }
    }

    // ---------- Java có sẵn trên máy ----------

    public async Task<string?> FindSystemJavaAsync(int major, CancellationToken ct)
    {
        var candidates = new List<string>();
        void Add(string? p)
        {
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p) &&
                !candidates.Contains(p, StringComparer.OrdinalIgnoreCase))
                candidates.Add(p);
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome)) Add(Path.Combine(javaHome, "bin", Platform.JavaExeName));

        foreach (var d in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var dir = d.Trim().Trim('"');
                // macOS: /usr/bin/java chỉ là "stub"; nếu máy chưa có Java nó bật hộp thoại đòi cài Java. Bỏ qua, đã quét thư mục JVM bên dưới.
                if (Platform.IsMac && dir == "/usr/bin") continue;
                Add(Path.Combine(dir, Platform.JavaExeName));
            }
            catch { }
        }

        foreach (var exe in PlatformJavaLocations()) Add(exe);

        foreach (var c in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (await GetMajorAsync(c, ct) == major) return c;
        }
        return null;
    }

    /// <summary>Các nơi thường cài Java trên từng hệ điều hành (trả về đường dẫn tới file java, có thể chưa tồn tại).</summary>
    private static IEnumerable<string> PlatformJavaLocations()
    {
        static IEnumerable<string> SubDirs(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : Array.Empty<string>(); }
            catch { return Array.Empty<string>(); }
        }

        var exeName = Platform.JavaExeName;

        if (Platform.IsWindows)
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
            var vendors = new[] { "Java", "Eclipse Adoptium", "Eclipse Foundation", "Microsoft", "Zulu",
                                  "Amazon Corretto", "BellSoft", "Semeru", "AdoptOpenJDK" };
            foreach (var root in roots)
                foreach (var vendor in vendors)
                    foreach (var sub in SubDirs(Path.Combine(root, vendor)))
                        yield return Path.Combine(sub, "bin", exeName);
            yield break;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // SDKMAN (cả macOS và Linux)
        foreach (var sub in SubDirs(Path.Combine(home, ".sdkman", "candidates", "java")))
            yield return Path.Combine(sub, "bin", exeName);

        if (Platform.IsMac)
        {
            // Bộ cài .pkg/.dmg: <tên>.jdk/Contents/Home/bin/java
            foreach (var root in new[] { "/Library/Java/JavaVirtualMachines",
                                         Path.Combine(home, "Library", "Java", "JavaVirtualMachines") })
                foreach (var jdk in SubDirs(root))
                    yield return Path.Combine(jdk, "Contents", "Home", "bin", exeName);

            // Homebrew: Apple Silicon /opt/homebrew, Intel /usr/local (openjdk, openjdk@17, openjdk@21...)
            foreach (var opt in new[] { "/opt/homebrew/opt", "/usr/local/opt" })
                foreach (var sub in SubDirs(opt))
                    if (Path.GetFileName(sub).StartsWith("openjdk", StringComparison.OrdinalIgnoreCase))
                        yield return Path.Combine(sub, "bin", exeName);
        }
        else
        {
            foreach (var sub in SubDirs("/usr/lib/jvm"))
                yield return Path.Combine(sub, "bin", exeName);
        }
    }

    /// <summary>Chạy "java -version" để biết số phiên bản chính (8, 17, 21...). Lỗi trả về -1.</summary>
    public static async Task<int> GetMajorAsync(string javaExe, CancellationToken ct)
    {
        Process? p = null;
        try
        {
            var psi = new ProcessStartInfo(javaExe, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            p = Process.Start(psi);
            if (p == null) return -1;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(8));
            var err = await p.StandardError.ReadToEndAsync(cts.Token);
            await p.WaitForExitAsync(cts.Token);

            var m = Regex.Match(err, @"version ""(\d+)(?:\.(\d+))?");
            if (!m.Success) return -1;
            var first = int.Parse(m.Groups[1].Value);
            if (first == 1 && m.Groups[2].Success) return int.Parse(m.Groups[2].Value); // 1.8.0 → 8
            return first;
        }
        catch { return -1; }
        finally
        {
            try { if (p != null && !p.HasExited) p.Kill(); } catch { }
            p?.Dispose();
        }
    }
}
