using System.Diagnostics;
using CmlLib.Core.Auth;
using KittyDy0Launcher;

// Bản dòng lệnh để thử phần lõi trên Mac (chưa có giao diện).
//   kittydy0 info
//   kittydy0 versions [--all]
//   kittydy0 java <số-Java>            ví dụ: kittydy0 java 21
//   kittydy0 play <phiên-bản> [tên] [--ram 4096]     ví dụ: kittydy0 play 1.21.1 Steve

var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    AppPaths.EnsureAll();
    switch (cmd)
    {
        case "info": Info(); break;
        case "versions": await VersionsAsync(args.Contains("--all")); break;
        case "java": await JavaAsync(args, cts.Token); break;
        case "play": await PlayAsync(args, cts.Token); break;
        default: Help(); break;
    }
}
catch (OperationCanceledException) { Console.WriteLine("Đã hủy."); }
catch (Exception ex)
{
    CrashLog.Write(ex, "cli");
    Console.Error.WriteLine("Lỗi: " + ex.Message);
    Environment.ExitCode = 1;
}

static void Help() => Console.WriteLine("""
    KittyDy0 Launcher (bản dòng lệnh)
      kittydy0 info                          xem hệ điều hành, CPU, thư mục dữ liệu
      kittydy0 versions [--all]              danh sách phiên bản (mặc định chỉ bản release)
      kittydy0 java <số>                     tìm/tải Java (8, 17, 21...) và in đường dẫn
      kittydy0 play <phiên bản> [tên] [--ram MB]   tải và chạy game (đăng nhập offline)
    """);

static void Info()
{
    Console.WriteLine($"Hệ điều hành : {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
    Console.WriteLine($"Kiến trúc    : {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
    Console.WriteLine($"Thư mục dữ liệu: {AppPaths.Root}");
}

static async Task VersionsAsync(bool all)
{
    var catalog = new VersionCatalog();
    var src = await catalog.LoadAsync();
    Console.WriteLine($"Nguồn danh sách: {src}");
    var list = all ? catalog.All : catalog.All.Where(v => v.Type == "release").ToList();
    foreach (var v in list.Take(all ? 60 : 25)) Console.WriteLine($"{v.Id,-14} {v.Type,-10} {v.Released:yyyy-MM-dd}");
    Console.WriteLine($"... tổng {list.Count} bản.");
}

static async Task JavaAsync(string[] a, CancellationToken ct)
{
    if (a.Length < 2 || !int.TryParse(a[1], out var major)) { Help(); return; }
    var jm = new JavaManager(Console.WriteLine);
    var exe = await jm.ResolveAsync(major, JavaSourceKind.Auto, "",
        (p, msg) => Console.WriteLine($"[{p,3:0}%] {msg}"), ct);
    Console.WriteLine(exe ?? "(Mojang sẽ tự tải Java)");
    if (exe != null) Console.WriteLine($"Phiên bản chính: {await JavaManager.GetMajorAsync(exe, ct)}");
}

static async Task PlayAsync(string[] a, CancellationToken ct)
{
    if (a.Length < 2) { Help(); return; }
    var versionId = a[1];
    var ram = 4096;
    var ri = Array.IndexOf(a, "--ram");
    if (ri >= 0 && ri + 1 < a.Length && int.TryParse(a[ri + 1], out var r)) ram = r;
    var name = a.Length > 2 && !a[2].StartsWith("--") ? a[2] : "Player";

    var cfg = AppConfig.Load();
    var catalog = new VersionCatalog();
    await catalog.LoadAsync();
    if (catalog.Find(versionId) == null) throw new InvalidOperationException($"Không có phiên bản \"{versionId}\". Dùng: kittydy0 versions --all");

    var inst = InstanceStore.LoadAll().FirstOrDefault(i => i.Name == "CLI " + versionId)
               ?? new Instance { Name = "CLI " + versionId, VersionId = versionId };
    inst.RamMb = ram;
    InstanceStore.Save(inst);

    var lastPct = -1;
    var runner = new GameRunner(catalog, Console.WriteLine);
    var proc = await runner.LaunchAsync(inst, MSession.CreateOfflineSession(name), null, cfg,
        (p, msg) => { var q = (int)p / 10; if (q != lastPct) { lastPct = q; Console.WriteLine($"[{p,3:0}%] {msg}"); } }, ct);

    Console.WriteLine("Game đang chạy. Nhấn Ctrl+C để dừng launcher (game vẫn chạy).");
    await proc.WaitForExitAsync(ct);
    Console.WriteLine($"Game đã thoát, mã {proc.ExitCode}.");
}
