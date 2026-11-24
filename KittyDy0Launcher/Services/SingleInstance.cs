using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace KittyDy0Launcher;

/// <summary>
/// Chỉ cho chạy một launcher tại một thời điểm (mỗi người dùng Windows):
/// - Mutex để biết đã có launcher đang chạy chưa.
/// - Named pipe để launcher thứ hai nhờ launcher đầu: "hiện cửa sổ lên" hoặc "mở file modpack này".
/// Dữ liệu qua pipe là dữ liệu không tin cậy: chỉ nhận đường dẫn file .mrpack/.zip có thật, và launcher
/// vẫn hỏi người dùng xác nhận trước khi nhập.
/// </summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\KittyDy0Launcher.SingleInstance.v1";
    private const int MaxMessageLength = 4096;
    private static Mutex? _mutex;

    // Tên pipe riêng theo từng người dùng, băm để không dính ký tự lạ trong tên tài khoản
    private static readonly string PipeName = "KittyDy0Launcher." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName)))[..16];

    /// <summary>Launcher được yêu cầu hiện cửa sổ lên (do một lần mở thứ hai gửi tới).</summary>
    public static event Action? ActivateRequested;

    /// <summary>Launcher được yêu cầu mở một file modpack (người dùng bấm đúp file .mrpack khi launcher đang chạy).</summary>
    public static event Action<string>? FileRequested;

    /// <summary>true nếu đây là launcher đầu tiên; false nếu đã có launcher khác đang chạy.</summary>
    public static bool TryAcquire()
    {
        _mutex = new Mutex(true, MutexName, out var createdNew);
        return createdNew;
    }

    /// <summary>Launcher đầu tiên gọi để lắng nghe yêu cầu từ các lần mở sau.</summary>
    public static void StartServer() => Task.Run(ServerLoop);

    private static async Task ServerLoop()
    {
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();

                using var reader = new StreamReader(server, new UTF8Encoding(false));
                var line = await reader.ReadLineAsync();
                if (line is { Length: > 0 and <= MaxMessageLength }) Dispatch(line);
            }
            catch
            {
                await Task.Delay(500); // pipe lỗi thì thử lại, không làm hỏng launcher
            }
        }
    }

    private static void Dispatch(string line)
    {
        var app = Application.Current;
        if (app == null) return;

        if (line == "ACTIVATE")
        {
            app.Dispatcher.InvokeAsync(() => ActivateRequested?.Invoke());
        }
        else if (line.StartsWith("OPEN\t", StringComparison.Ordinal))
        {
            var path = line[5..];
            if (IsModpackFile(path))
                app.Dispatcher.InvokeAsync(() => FileRequested?.Invoke(path));
        }
    }

    /// <summary>File modpack hợp lệ: đường dẫn tuyệt đối, đuôi .mrpack hoặc .zip, và tồn tại.</summary>
    public static bool IsModpackFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var ext = Path.GetExtension(path);
            return Path.IsPathRooted(path) &&
                   (ext.Equals(".mrpack", StringComparison.OrdinalIgnoreCase) || ext.Equals(".zip", StringComparison.OrdinalIgnoreCase)) &&
                   File.Exists(path);
        }
        catch { return false; }
    }

    // ---------- Phía launcher thứ hai ----------

    public static bool SendActivate() => Send("ACTIVATE");
    public static bool SendOpen(string path) => Send("OPEN\t" + Path.GetFullPath(path));

    private static bool Send(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(message);
            return true;
        }
        catch { return false; }
    }
}
