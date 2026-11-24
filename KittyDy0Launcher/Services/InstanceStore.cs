using System.Text.Json;

namespace KittyDy0Launcher;

/// <summary>Đọc/ghi các instance: mỗi instance là một thư mục có instance.json và thư mục minecraft.</summary>
public static class InstanceStore
{
    public static List<Instance> LoadAll()
    {
        var list = new List<Instance>();
        if (!Directory.Exists(AppPaths.Instances)) return list;

        foreach (var dir in Directory.GetDirectories(AppPaths.Instances))
        {
            var file = Path.Combine(dir, "instance.json");
            try
            {
                if (!File.Exists(file)) continue;
                var inst = JsonSerializer.Deserialize<Instance>(File.ReadAllText(file), AppConfig.JsonOptions);
                if (inst == null) continue;
                inst.Id = Path.GetFileName(dir); // id luôn trùng tên thư mục
                list.Add(inst);
            }
            catch { /* bỏ qua instance hỏng */ }
        }
        return list.OrderBy(i => i.Created).ToList();
    }

    public static void Save(Instance inst)
    {
        Directory.CreateDirectory(inst.GameDir);
        File.WriteAllText(Path.Combine(inst.Dir, "instance.json"),
            JsonSerializer.Serialize(inst, AppConfig.JsonOptions));
    }

    public static void Delete(Instance inst)
    {
        if (Directory.Exists(inst.Dir)) Directory.Delete(inst.Dir, true);
    }

    /// <summary>Nhân bản cả cấu hình lẫn thư mục game (world, mod, cài đặt).</summary>
    public static Instance Duplicate(Instance src)
    {
        var copy = new Instance
        {
            Name = src.Name + " (bản sao)",
            VersionId = src.VersionId,
            RamMb = src.RamMb,
            JavaSource = src.JavaSource,
            JavaPath = src.JavaPath,
            JvmArgs = src.JvmArgs,
            AutoJoin = src.AutoJoin,
            LoaderType = src.LoaderType,
            LoaderVersion = src.LoaderVersion,
            LaunchId = src.LaunchId,
            Created = DateTime.Now
        };
        Save(copy);
        if (Directory.Exists(src.GameDir)) CopyDir(src.GameDir, copy.GameDir);
        return copy;
    }

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(from))
            CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
