namespace KittyDy0Launcher;

/// <summary>Ghi lỗi chưa xử lý vào crash.log (cạnh config.json) để biết nguyên nhân khi launcher gặp sự cố.</summary>
public static class CrashLog
{
    public static string FilePath => Path.Combine(AppPaths.Root, "crash.log");

    public static void Write(Exception? ex, string source)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var fi = new FileInfo(FilePath);
            if (fi.Exists && fi.Length > 512 * 1024) fi.Delete(); // giữ file nhỏ gọn
            // UTF-8 có BOM: Notepad của Windows 10 nhận ra đúng tiếng Việt (không có BOM thì hiện chữ lạ kiểu "KhÃºc xáº¡")
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ({source})\n{ex}\n\n", new System.Text.UTF8Encoding(true));
        }
        catch { /* không ghi được log cũng không được làm lỗi thêm */ }
    }
}
