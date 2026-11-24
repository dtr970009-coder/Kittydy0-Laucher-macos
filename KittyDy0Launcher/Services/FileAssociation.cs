using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace KittyDy0Launcher;

/// <summary>
/// Đăng ký launcher làm ứng dụng mở file .mrpack. Chỉ ghi vào HKEY_CURRENT_USER nên không cần quyền admin
/// và không đụng tới máy của người khác. Bấm đúp .mrpack thì Windows chạy: "launcher.exe" "đường-dẫn-file".
/// Lưu ý: nếu người dùng đã chọn sẵn một ứng dụng mặc định khác cho .mrpack, Windows giữ lựa chọn đó;
/// khi ấy launcher vẫn xuất hiện trong "Mở bằng" và người dùng chọn "Luôn dùng ứng dụng này".
/// </summary>
public static class FileAssociation
{
    private const string Ext = ".mrpack";
    private const string ProgId = "KittyDy0Launcher.mrpack";

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    private static string? ExePath => Environment.ProcessPath;
    private static string CommandFor(string exe) => $"\"{exe}\" \"%1\"";

    /// <summary>Đã đăng ký và đang trỏ đúng tới file exe hiện tại (di chuyển exe thì coi như chưa đăng ký).</summary>
    public static bool IsRegistered()
    {
        try
        {
            var exe = ExePath;
            if (string.IsNullOrEmpty(exe)) return false;
            using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ProgId}\shell\open\command");
            return string.Equals(k?.GetValue("") as string, CommandFor(exe), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <param name="force">true: đặt launcher làm mặc định cho .mrpack kể cả khi đã có ứng dụng khác; false: chỉ đặt khi chưa có ai.</param>
    public static bool Register(bool force)
    {
        try
        {
            var exe = ExePath;
            if (string.IsNullOrEmpty(exe)) return false;

            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
                k.SetValue("", "Modpack Modrinth (KittyDy0 Launcher)");
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\DefaultIcon"))
                k.SetValue("", $"\"{exe}\",0");
            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}\shell\open\command"))
                k.SetValue("", CommandFor(exe));

            using (var k = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Ext}"))
            {
                var current = k.GetValue("") as string;
                if (force || string.IsNullOrEmpty(current)) k.SetValue("", ProgId);
                using var ow = k.CreateSubKey("OpenWithProgids");
                ow.SetValue(ProgId, "", RegistryValueKind.String); // hiện trong danh sách "Mở bằng"
            }

            Notify();
            return true;
        }
        catch { return false; }
    }

    /// <summary>Gỡ đăng ký của launcher, không đụng tới đăng ký của ứng dụng khác.</summary>
    public static bool Unregister()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Ext}", writable: true))
            {
                if (k != null)
                {
                    if (string.Equals(k.GetValue("") as string, ProgId, StringComparison.OrdinalIgnoreCase))
                        k.DeleteValue("", false);
                    using var ow = k.OpenSubKey("OpenWithProgids", writable: true);
                    ow?.DeleteValue(ProgId, false);
                }
            }
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false);
            Notify();
            return true;
        }
        catch { return false; }
    }

    private static void Notify()
    {
        try { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); } // SHCNE_ASSOCCHANGED: báo Explorer đọc lại
        catch { }
    }
}
