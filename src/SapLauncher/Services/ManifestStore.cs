using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>manifest.json 读写：系统清单、分组、脚本登记与设置（不含任何密钥）。</summary>
public sealed class ManifestStore
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ManifestData Data { get; private set; } = new();

    public void Load()
    {
        AppPaths.EnsureAll();
        try
        {
            if (File.Exists(AppPaths.Manifest))
            {
                var json = File.ReadAllText(AppPaths.Manifest);
                var data = JsonSerializer.Deserialize<ManifestData>(json, Options);
                if (data is not null) Data = data;
            }
        }
        catch (Exception ex)
        {
            Log.Error("读取 manifest.json 失败，使用空清单", ex);
            Data = new ManifestData();
        }

        Data.Settings ??= new AppSettings();
        Data.Systems ??= new List<SapSystem>();
        Data.Groups ??= new List<GroupDef>();
        Data.Scripts ??= new List<ScriptDef>();
    }

    public void Save()
    {
        AppPaths.EnsureAll();
        try
        {
            var json = JsonSerializer.Serialize(Data, Options);
            var tmp = AppPaths.Manifest + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, AppPaths.Manifest, true);
        }
        catch (Exception ex)
        {
            Log.Error("保存 manifest.json 失败", ex);
        }
    }

    /// <summary>同步前快照，保留最近 5 份，可回滚。</summary>
    public void Snapshot()
    {
        try
        {
            AppPaths.EnsureAll();
            if (!File.Exists(AppPaths.Manifest)) return;
            var target = Path.Combine(AppPaths.Backups, $"manifest-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(AppPaths.Manifest, target, true);

            var old = Directory.GetFiles(AppPaths.Backups, "manifest-*.json")
                .OrderByDescending(File.GetLastWriteTime).Skip(5).ToList();
            foreach (var f in old)
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("创建同步快照失败：" + ex.Message);
        }
    }

    public IEnumerable<string> BackupFiles => Directory.Exists(AppPaths.Backups)
        ? Directory.GetFiles(AppPaths.Backups, "manifest-*.json").OrderByDescending(File.GetLastWriteTime)
        : Enumerable.Empty<string>();

    public bool RestoreLatestBackup(out string message)
    {
        var latest = BackupFiles.FirstOrDefault();
        if (latest is null)
        {
            message = "没有可用快照";
            return false;
        }

        try
        {
            var json = File.ReadAllText(latest);
            var data = JsonSerializer.Deserialize<ManifestData>(json, Options);
            if (data is null) { message = "快照内容无效"; return false; }
            Data = data;
            Save();
            message = "已回滚到 " + Path.GetFileName(latest);
            return true;
        }
        catch (Exception ex)
        {
            message = "回滚失败：" + ex.Message;
            return false;
        }
    }
}
