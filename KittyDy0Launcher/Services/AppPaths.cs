namespace KittyDy0Launcher;

/// <summary>
/// Các thư mục dữ liệu của launcher: %AppData%\KittyDy0Launcher (Windows),
/// ~/Library/Application Support/KittyDy0Launcher (macOS), ~/.local/share/KittyDy0Launcher (Linux).
/// </summary>
public static class AppPaths
{
    public static readonly string Root = Platform.DataRoot();

    public static string ConfigFile => Path.Combine(Root, "config.json");
    public static string Instances => Path.Combine(Root, "instances"); // mỗi instance một thư mục riêng
    public static string Shared => Path.Combine(Root, "shared");       // libraries, assets, versions dùng chung (đỡ tải lại)
    public static string Java => Path.Combine(Root, "java");           // Java do launcher tải
    public static string Cache => Path.Combine(Root, "cache");

    public static void EnsureAll()
    {
        foreach (var d in new[] { Root, Instances, Shared, Java, Cache })
            Directory.CreateDirectory(d);
    }
}
