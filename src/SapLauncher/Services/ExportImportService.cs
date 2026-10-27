using System.IO;
using System.Text;
using System.Text.Json;
using SapLauncher.Models;

namespace SapLauncher.Services;

public class ExportSystem
{
    public string Name { get; set; } = "";
    public string Sid { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3200;
    public string Router { get; set; } = "";
    public string MessageServer { get; set; } = "";
    public ConnKind Kind { get; set; }
    public string Memo { get; set; } = "";
    public string Group { get; set; } = "";
    public string Alias { get; set; } = "";
    public EnvKind Env { get; set; }
    public string LastClient { get; set; } = "";
    public bool DirectTcode { get; set; }
    public string LoginMethod { get; set; } = "Auto";
    public string Uuid { get; set; } = "";
}

public class ExportBundle
{
    public int Version { get; set; } = 1;
    public string App { get; set; } = "SapLauncher";
    public DateTime ExportedAt { get; set; } = DateTime.Now;
    public string Machine { get; set; } = Environment.MachineName;
    public List<ExportSystem> Systems { get; set; } = new();
    public List<GroupDef> Groups { get; set; } = new();
    public bool IncludesCredentials { get; set; }
    public string? CredentialsBlob { get; set; }
}

public class ImportOptions
{
    public bool OverwriteExisting { get; set; }
    public bool ImportGroups { get; set; } = true;
}

public class ImportPreviewItem
{
    public string Name { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}

/// <summary>连接导出/导入（默认不含凭据；含凭据时用独立密码 AES-256-GCM 加密）。</summary>
public static class ExportImportService
{
    public static void Export(string path, IEnumerable<SapSystem> systems, IEnumerable<GroupDef> groups,
        VaultService? vault, string? exportPassword)
    {
        var bundle = new ExportBundle
        {
            Systems = systems.Select(s => new ExportSystem
            {
                Name = s.Name,
                Sid = s.Sid,
                Host = s.Host,
                Port = s.Port,
                Router = s.Router,
                MessageServer = s.MessageServer,
                Kind = s.Kind,
                Memo = s.Memo,
                Group = s.Group,
                Alias = s.Alias,
                Env = s.Env,
                LastClient = s.LastClient,
                DirectTcode = s.DirectTcode,
                LoginMethod = s.LoginMethod,
                Uuid = s.Uuid
            }).ToList(),
            Groups = groups.ToList()
        };

        if (!string.IsNullOrWhiteSpace(exportPassword) && vault is not null && vault.IsUnlocked)
        {
            var creds = vault.Items.Select(c => new Credential
            {
                SystemUuid = c.SystemUuid,
                Client = c.Client,
                User = c.User,
                DisplayName = c.DisplayName,
                Password = c.Password,
                Remember = c.Remember,
                Language = c.Language,
                Tcode = c.Tcode,
                LogonGroup = c.LogonGroup,
                UpdatedAt = c.UpdatedAt
            }).ToList();

            var json = JsonSerializer.Serialize(new VaultPayload { Items = creds }, ManifestStore.Options);
            bundle.CredentialsBlob = VaultService.EncryptForExport(json, exportPassword);
            bundle.IncludesCredentials = true;
        }

        var text = JsonSerializer.Serialize(bundle, ManifestStore.Options);
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    public static ExportBundle? Read(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ExportBundle>(json, ManifestStore.Options);
        }
        catch (Exception ex)
        {
            Log.Warn("读取导入文件失败：" + ex.Message);
            return null;
        }
    }

    public static List<ImportPreviewItem> Preview(ExportBundle bundle, IReadOnlyList<SapSystem> existing,
        ImportOptions options)
    {
        var list = new List<ImportPreviewItem>();
        foreach (var s in bundle.Systems)
        {
            var hit = existing.FirstOrDefault(e =>
                string.Equals(e.Name, s.Name, StringComparison.CurrentCultureIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(e.Host) && string.Equals(e.Host, s.Host, StringComparison.OrdinalIgnoreCase)
                                                   && string.Equals(e.Sid, s.Sid, StringComparison.OrdinalIgnoreCase)));

            list.Add(new ImportPreviewItem
            {
                Name = s.Name,
                Action = hit is null ? "新增" : options.OverwriteExisting ? "覆盖" : "跳过",
                Detail = $"{(string.IsNullOrWhiteSpace(s.Sid) ? "-" : s.Sid)} · {(string.IsNullOrWhiteSpace(s.Host) ? s.MessageServer : s.Host + ":" + s.Port)}"
            });
        }
        return list;
    }

    public static (int added, int updated, int skipped) Apply(ExportBundle bundle, List<SapSystem> systems,
        List<GroupDef> groups, ImportOptions options)
    {
        int added = 0, updated = 0, skipped = 0;

        if (options.ImportGroups)
        {
            foreach (var g in bundle.Groups)
            {
                if (string.IsNullOrWhiteSpace(g.Name)) continue;
                if (groups.Any(x => string.Equals(x.Name, g.Name, StringComparison.CurrentCultureIgnoreCase))) continue;
                groups.Add(new GroupDef { Name = g.Name, SortOrder = groups.Count });
            }
        }

        foreach (var s in bundle.Systems)
        {
            var hit = systems.FirstOrDefault(e =>
                string.Equals(e.Name, s.Name, StringComparison.CurrentCultureIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(e.Host) && string.Equals(e.Host, s.Host, StringComparison.OrdinalIgnoreCase)
                                                   && string.Equals(e.Sid, s.Sid, StringComparison.OrdinalIgnoreCase)));

            if (hit is not null)
            {
                if (!options.OverwriteExisting) { skipped++; continue; }
                hit.Name = s.Name;
                hit.Sid = s.Sid;
                hit.Host = s.Host;
                hit.Port = s.Port;
                hit.Router = s.Router;
                hit.MessageServer = s.MessageServer;
                hit.Kind = s.Kind;
                hit.Memo = s.Memo;
                hit.Group = s.Group;
                hit.Alias = s.Alias;
                hit.Env = s.Env;
                hit.LastClient = s.LastClient;
                hit.DirectTcode = s.DirectTcode;
                hit.LoginMethod = s.LoginMethod;
                updated++;
                continue;
            }

            systems.Add(new SapSystem
            {
                Uuid = string.IsNullOrWhiteSpace(s.Uuid) ? "local:" + Guid.NewGuid().ToString("N") : s.Uuid,
                Name = s.Name,
                Sid = s.Sid,
                Host = s.Host,
                Port = s.Port,
                Router = s.Router,
                MessageServer = s.MessageServer,
                Kind = s.Kind,
                Memo = s.Memo,
                Group = s.Group,
                Alias = s.Alias,
                Env = s.Env,
                EnvManual = s.Env != EnvKind.Unknown,
                LastClient = s.LastClient,
                DirectTcode = s.DirectTcode,
                LoginMethod = s.LoginMethod,
                IsLocal = true,
                SortOrder = systems.Count
            });
            added++;
        }

        return (added, updated, skipped);
    }

    public static int ImportCredentials(ExportBundle bundle, VaultService vault, string password)
    {
        if (string.IsNullOrWhiteSpace(bundle.CredentialsBlob) || !vault.IsUnlocked) return 0;
        var json = VaultService.DecryptFromExport(bundle.CredentialsBlob, password);
        if (json is null) return -1;

        var payload = JsonSerializer.Deserialize<VaultPayload>(json, ManifestStore.Options);
        if (payload is null) return -1;

        int count = 0;
        foreach (var c in payload.Items)
        {
            vault.Upsert(c);
            count++;
        }
        return count;
    }

    /// <summary>生成可发给同事的连接信息文本（不含密码）。</summary>
    public static string BuildShareText(SapSystem s, string? user)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"【SAP 连接】{s.DisplayName}");
        sb.AppendLine($"系统 ID：{s.Sid}");
        sb.AppendLine(s.Kind switch
        {
            ConnKind.LoadBalanced => $"连接方式：消息服务器（{s.MessageServer}）",
            ConnKind.Router => $"连接方式：经 Router → {s.Host}:{s.Port}\nRouter：{s.Router}",
            _ => $"连接方式：直连 {s.Host}:{s.Port}"
        });
        if (!string.IsNullOrWhiteSpace(user)) sb.AppendLine($"账号：{user}");
        if (!string.IsNullOrWhiteSpace(s.LastClient)) sb.AppendLine($"常用客户端：{s.LastClient}");
        if (!string.IsNullOrWhiteSpace(s.Memo)) sb.AppendLine($"备注：{s.Memo}");
        sb.Append("（密码请另行获取，此信息不含密码）");
        return sb.ToString();
    }
}
