using System.Diagnostics;
using System.Text.RegularExpressions;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;

namespace KittyDy0Launcher;

/// <summary>
/// Chuẩn bị Java, tải game bằng CmlLib.Core vào thư mục dùng chung, rồi chạy game
/// với thư mục dữ liệu riêng của từng instance.
/// </summary>
public class GameRunner
{
    public const string ServerHost = "kittydy0.online";

    private readonly VersionCatalog _catalog;
    private readonly Action<string> _log;

    public GameRunner(VersionCatalog catalog, Action<string> log)
    {
        _catalog = catalog;
        _log = log;
    }

    /// <summary>Tải (nếu thiếu) và khởi động game. Trả về tiến trình đang chạy.</summary>
    /// <param name="session">Phiên đăng nhập (offline, Microsoft hoặc Ely.by).</param>
    /// <param name="extraJvmArg">Tham số JVM thêm, vd -javaagent của authlib-injector khi dùng Ely.by (có thể null).</param>
    public async Task<Process> LaunchAsync(Instance inst, MSession session, string? extraJvmArg, AppConfig cfg,
        Action<double, string> report, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(inst.VersionId))
            throw new InvalidOperationException("Instance chưa chọn phiên bản.");

        // 1. Java
        report(0, "Đang kiểm tra Java...");
        var major = await _catalog.GetRequiredJavaMajorAsync(inst.VersionId, ct);
        var source = inst.JavaSource == JavaSourceKind.Global ? cfg.JavaSource : inst.JavaSource;
        _log($"Phiên bản {inst.VersionId} cần Java {JavaManager.Normalize(major)}. Nguồn Java: {source}");

        var javaExe = await new JavaManager(_log).ResolveAsync(major, source, inst.JavaPath, report, ct);
        _log(javaExe == null ? "Dùng Java do thư viện (Mojang) tự tải." : "Dùng Java: " + javaExe);
        ct.ThrowIfCancellationRequested();

        // Có mod loader thì chạy phiên bản của loader (kế thừa từ bản Minecraft gốc)
        var launchId = string.IsNullOrEmpty(inst.LaunchId) ? inst.VersionId : inst.LaunchId;
        if (launchId != inst.VersionId) _log($"Chạy phiên bản mod loader: {launchId}");

        // 2. Tải game vào thư mục dùng chung (libraries, assets, versions dùng chung giữa các instance)
        Directory.CreateDirectory(inst.GameDir);
        Directory.CreateDirectory(AppPaths.Shared);

        var launcher = new MinecraftLauncher(new MinecraftPath(AppPaths.Shared));
        launcher.FileProgressChanged += (_, e) =>
        {
            var pct = e.TotalTasks > 0 ? (double)e.ProgressedTasks / e.TotalTasks * 100 : 0;
            report(pct, $"Đang tải {e.Name} ({e.ProgressedTasks}/{e.TotalTasks})");
        };

        report(0, $"Đang kiểm tra và tải {inst.VersionId}...");
        var option = new MLaunchOption
        {
            Session = session,
            MaximumRamMb = inst.RamMb
        };
        var process = await launcher.InstallAndBuildProcessAsync(launchId, option);
        ct.ThrowIfCancellationRequested();

        // 3. Chỉnh tiến trình: Java riêng, thư mục game riêng của instance, tham số thêm
        var psi = process.StartInfo;
        if (javaExe != null)
        {
            psi.FileName = javaExe;
        }
        else if (psi.FileName.EndsWith("javaw.exe", StringComparison.OrdinalIgnoreCase))
        {
            var console = Path.Combine(Path.GetDirectoryName(psi.FileName) ?? "", "java.exe");
            if (File.Exists(console)) psi.FileName = console; // java.exe để đọc được log
        }

        psi.WorkingDirectory = inst.GameDir;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        ReplaceGameDir(psi, inst.GameDir);
        PrependJvmArgs(psi, inst.JvmArgs);
        PrependJvmArgs(psi, extraJvmArg); // đặt sau cùng nên nằm đầu dòng lệnh, -javaagent phải đứng trước main class
        if (inst.AutoJoin)
        {
            var serverArgs = ServerArgs(inst.VersionId, ServerHost);
            if (serverArgs.Length > 0) AppendArgs(psi, serverArgs);
            else _log("Phiên bản này không hỗ trợ tự vào server, bỏ qua.");
        }

        process.EnableRaisingEvents = true;
        process.OutputDataReceived += (_, e) => { if (e.Data != null) _log(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) _log(e.Data); };

        _log($"Khởi động game tại {inst.GameDir}");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    // ---------- Xử lý tham số dòng lệnh ----------

    /// <summary>Đổi --gameDir sang thư mục riêng của instance.</summary>
    private static void ReplaceGameDir(ProcessStartInfo psi, string dir)
    {
        if (psi.ArgumentList.Count > 0)
        {
            for (var i = 0; i < psi.ArgumentList.Count - 1; i++)
            {
                if (psi.ArgumentList[i] == "--gameDir")
                {
                    psi.ArgumentList[i + 1] = dir;
                    return;
                }
            }
        }
        else if (!string.IsNullOrEmpty(psi.Arguments))
        {
            psi.Arguments = Regex.Replace(psi.Arguments, @"--gameDir\s+(?:""[^""]*""|\S+)",
                _ => $"--gameDir \"{dir}\"");
        }
    }

    /// <summary>Thêm tham số JVM của người dùng vào đầu dòng lệnh (trước tên class chính).</summary>
    private static void PrependJvmArgs(ProcessStartInfo psi, string? extra)
    {
        if (string.IsNullOrWhiteSpace(extra)) return;
        if (psi.ArgumentList.Count > 0)
        {
            var idx = 0;
            foreach (var a in SplitArgs(extra)) psi.ArgumentList.Insert(idx++, a);
        }
        else
        {
            psi.Arguments = extra.Trim() + " " + psi.Arguments;
        }
    }

    private static void AppendArgs(ProcessStartInfo psi, string[] args)
    {
        if (psi.ArgumentList.Count > 0)
        {
            foreach (var a in args) psi.ArgumentList.Add(a);
        }
        else
        {
            psi.Arguments += " " + string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        }
    }

    private static IEnumerable<string> SplitArgs(string s) =>
        Regex.Matches(s, @"""([^""]*)""|(\S+)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);

    /// <summary>Tham số tự vào server tùy phiên bản: 1.20+ dùng quickPlay, 1.6–1.19 dùng --server.</summary>
    private static string[] ServerArgs(string id, string host)
    {
        string[] quick = { "--quickPlayMultiplayer", host };
        string[] legacy = { "--server", host, "--port", "25565" };

        var m = Regex.Match(id, @"^1\.(\d+)");
        if (m.Success)
        {
            var minor = int.Parse(m.Groups[1].Value);
            if (minor >= 20) return quick;
            if (minor >= 6) return legacy;
            return Array.Empty<string>();
        }

        var s = Regex.Match(id, @"^(\d{2})w(\d{2})");
        if (s.Success)
        {
            var year = int.Parse(s.Groups[1].Value);
            var week = int.Parse(s.Groups[2].Value);
            return year > 23 || (year == 23 && week >= 14) ? quick : legacy;
        }

        if (Regex.IsMatch(id, @"^\d{2}\.\d+")) return quick; // kiểu đánh số mới
        return Array.Empty<string>();
    }
}
