using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KittyDy0Launcher;

/// <summary>Khác biệt giữa Windows / macOS / Linux gom về một chỗ (đường dẫn, tên file Java, mở thư mục...).</summary>
public static class Platform
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsMac => OperatingSystem.IsMacOS();
    public static bool IsLinux => OperatingSystem.IsLinux();

    public static string JavaExeName => IsWindows ? "java.exe" : "java";

    /// <summary>Tên hệ điều hành theo API Adoptium: windows / mac / linux.</summary>
    public static string AdoptiumOs => IsWindows ? "windows" : IsMac ? "mac" : "linux";

    /// <summary>Tên kiến trúc theo API Adoptium: x64 / aarch64.</summary>
    public static string AdoptiumArch => RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x64";

    public static bool IsArm64 => RuntimeInformation.OSArchitecture == Architecture.Arm64;

    /// <summary>Thư mục dữ liệu của launcher: %AppData% (Windows), ~/Library/Application Support (macOS), ~/.local/share (Linux).</summary>
    public static string DataRoot()
    {
        const string name = "KittyDy0Launcher";
        if (IsWindows)
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (IsMac)
            return Path.Combine(home, "Library", "Application Support", name);

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var baseDir = string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".local", "share") : xdg;
        return Path.Combine(baseDir, name);
    }

    /// <summary>Mở thư mục/file bằng ứng dụng mặc định (Explorer / Finder / xdg-open).</summary>
    public static void OpenPath(string path)
    {
        if (IsWindows)
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        else
            RunOpener(path);
    }

    /// <summary>Mở link https bằng trình duyệt mặc định.</summary>
    public static void OpenUrl(string url)
    {
        if (IsWindows)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        else
            RunOpener(url);
    }

    private static void RunOpener(string target)
    {
        var psi = new ProcessStartInfo(IsMac ? "open" : "xdg-open") { UseShellExecute = false };
        psi.ArgumentList.Add(target); // ArgumentList: không cần lo dấu cách trong "Application Support"
        Process.Start(psi);
    }
}
