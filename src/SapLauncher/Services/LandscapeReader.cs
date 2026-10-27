using System.Globalization;
using System.IO;
using System.Xml.Linq;
using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>
/// 解析 SAP Logon 配置：新版 SAPUILandscape.xml（含 &lt;Includes&gt; 引用）与旧版 saplogon.ini。
/// 只读，绝不回写。
/// </summary>
public static class LandscapeReader
{
    public static LandscapeScanResult Scan()
    {
        var result = new LandscapeScanResult();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var routers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var messageServers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        foreach (var path in SapGuiLocator.LandscapeCandidates())
        {
            if (!File.Exists(path)) continue;
            if (path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            {
                ReadIni(path, result);
                continue;
            }
            ParseXml(path, routers, messageServers, result, order, seenFiles, 0);
        }

        // 按 Workspaces 顺序重排，保持与 SAP Logon 一致
        if (order.Count > 0)
        {
            var rank = order.Select((id, i) => (id, i)).GroupBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
            result.Entries.Sort((a, b) =>
            {
                var ra = rank.TryGetValue(a.Uuid, out var ia) ? ia : int.MaxValue;
                var rb = rank.TryGetValue(b.Uuid, out var ib) ? ib : int.MaxValue;
                if (ra != rb) return ra.CompareTo(rb);
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
        }

        for (int i = 0; i < result.Entries.Count; i++) result.Entries[i].Order = i;
        return result;
    }

    private static void ParseXml(string path, Dictionary<string, string> routers,
        Dictionary<string, string> messageServers, LandscapeScanResult result,
        List<string> order, HashSet<string> seen, int depth)
    {
        if (depth > 5) return;
        if (!seen.Add(path)) return; // 防环

        XDocument? doc = null;
        Exception? last = null;
        for (int attempt = 0; attempt < 3 && doc is null; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                doc = XDocument.Load(fs, LoadOptions.None);
            }
            catch (Exception ex)
            {
                last = ex;
                Thread.Sleep(300); // SAP Logon 可能正在写入，短暂重试
            }
        }

        if (doc is null)
        {
            result.Warnings.Add($"{Path.GetFileName(path)} 解析失败：{last?.Message}");
            Log.Warn($"解析 {path} 失败：{last?.Message}");
            return;
        }

        var root = doc.Root;
        if (root is null) return;
        if (!result.Sources.Contains(path)) result.Sources.Add(path);

        foreach (var r in root.Descendants("Router"))
        {
            var uuid = (string?)r.Attribute("uuid");
            var value = (string?)r.Attribute("router") ?? (string?)r.Attribute("name");
            if (!string.IsNullOrWhiteSpace(uuid) && !string.IsNullOrWhiteSpace(value))
                routers[uuid!] = value!;
        }

        foreach (var m in root.Descendants("Messageserver"))
        {
            var uuid = (string?)m.Attribute("uuid");
            var host = (string?)m.Attribute("host");
            if (!string.IsNullOrWhiteSpace(uuid) && !string.IsNullOrWhiteSpace(host))
                messageServers[uuid!] = host!;
        }

        // 工作区顺序
        foreach (var item in root.Descendants("Item"))
        {
            var sid = (string?)item.Attribute("serviceid");
            if (!string.IsNullOrWhiteSpace(sid)) order.Add(sid!);
        }

        foreach (var svc in root.Descendants("Service"))
        {
            var type = (string?)svc.Attribute("type");
            if (!string.IsNullOrWhiteSpace(type) && !type!.Equals("SAPGUI", StringComparison.OrdinalIgnoreCase))
                continue;

            var uuid = (string?)svc.Attribute("uuid") ?? Guid.NewGuid().ToString();
            var name = (string?)svc.Attribute("name") ?? "";
            var systemId = (string?)svc.Attribute("systemid") ?? "";
            var server = (string?)svc.Attribute("server") ?? "";
            var routerId = (string?)svc.Attribute("routerid") ?? "";
            var msId = (string?)svc.Attribute("msid") ?? "";
            var memo = svc.Element("Memo")?.Value ?? "";

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(systemId)) continue;
            if (string.IsNullOrWhiteSpace(name)) name = systemId;

            var entry = new LandscapeEntry
            {
                Uuid = uuid,
                Name = name.Trim(),
                Sid = systemId.Trim(),
                Memo = memo,
                SourceFile = path
            };

            if (!string.IsNullOrWhiteSpace(msId) && messageServers.TryGetValue(msId, out var msHost))
            {
                entry.Kind = ConnKind.LoadBalanced;
                entry.MessageServer = msHost;
                entry.Host = msHost;
                entry.Port = 3600;
            }
            else
            {
                var (host, port) = SplitHostPort(server);
                entry.Host = host;
                entry.Port = port;
                entry.Kind = ConnKind.Direct;
            }

            if (!string.IsNullOrWhiteSpace(routerId) && routers.TryGetValue(routerId, out var router))
            {
                entry.Router = router;
                if (entry.Kind != ConnKind.LoadBalanced) entry.Kind = ConnKind.Router;
            }

            // 同一 uuid 以先读到的为准（用户级配置优先）
            if (result.Entries.Any(e => string.Equals(e.Uuid, entry.Uuid, StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Entries.Add(entry);
        }

        var includes = root.Element("Includes")?.Elements("Include") ?? Enumerable.Empty<XElement>();
        foreach (var inc in includes)
        {
            var url = (string?)inc.Attribute("url");
            if (string.IsNullOrWhiteSpace(url)) continue;
            var local = ToLocalPath(url!);
            if (local is not null && File.Exists(local))
                ParseXml(local, routers, messageServers, result, order, seen, depth + 1);
        }
    }

    private static string? ToLocalPath(string url)
    {
        try
        {
            if (url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
                return new Uri(url).LocalPath;
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                return new Uri(url).LocalPath;
            return Path.IsPathRooted(url) ? url : null;
        }
        catch { return null; }
    }

    public static (string host, int port) SplitHostPort(string server)
    {
        if (string.IsNullOrWhiteSpace(server)) return ("", 3200);
        var s = server.Trim();
        var idx = s.LastIndexOf(':');
        if (idx > 0 && idx < s.Length - 1 && int.TryParse(s[(idx + 1)..], out var port))
            return (s[..idx], port);
        return (s, 3200);
    }

    // ---------------- 旧版 saplogon.ini ----------------

    private static void ReadIni(string path, LandscapeScanResult result)
    {
        try
        {
            var lines = File.ReadAllLines(path);
            var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string current = "";
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    current = line[1..^1];
                    if (!sections.ContainsKey(current)) sections[current] = new(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                var eq = line.IndexOf('=');
                if (eq <= 0 || current.Length == 0) continue;
                sections[current][line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }

            if (!sections.TryGetValue("Description", out var descriptions)) return;

            foreach (var (key, name) in descriptions)
            {
                var sid = sections.GetValueOrDefault("System")?.GetValueOrDefault(key) ?? "";
                var server = sections.GetValueOrDefault("Server")?.GetValueOrDefault(key) ?? "";
                var router = sections.GetValueOrDefault("Router")?.GetValueOrDefault(key) ?? "";
                var (host, port) = SplitHostPort(server);

                result.Entries.Add(new LandscapeEntry
                {
                    Uuid = "ini:" + key + ":" + name,
                    Name = name,
                    Sid = sid,
                    Host = host,
                    Port = port,
                    Router = router,
                    Kind = string.IsNullOrWhiteSpace(router) ? ConnKind.Direct : ConnKind.Router,
                    SourceFile = path,
                    Order = result.Entries.Count
                });
            }

            if (!result.Sources.Contains(path)) result.Sources.Add(path);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"{Path.GetFileName(path)} 解析失败：{ex.Message}");
        }
    }

    /// <summary>推断环境类型（DEV / QAS / PRD），仅基于名称与 SID 的显式标识。</summary>
    public static EnvKind InferEnv(string name, string sid)
    {
        var text = (name + " " + sid).ToUpperInvariant();

        bool Has(params string[] keys) => keys.Any(k => text.Contains(k, StringComparison.Ordinal));

        if (Has("PRD", "PROD", "生产", "PRODUCTION")) return EnvKind.Prd;
        if (Has("QAS", "QUAL", "TEST", "测试", "UAT", "QA")) return EnvKind.Qas;
        if (Has("DEV", "DEVELOP", "开发", "SBX", "SANDBOX")) return EnvKind.Dev;

        // SID 形如 S4P / P4P 末位 P 通常为生产；S4Q 测试；S4D 开发
        var sidUp = sid.ToUpperInvariant();
        if (sidUp.Length >= 2)
        {
            if (sidUp.EndsWith("P") && sidUp.StartsWith("S")) return EnvKind.Prd;
            if (sidUp.EndsWith("Q") && sidUp.StartsWith("S")) return EnvKind.Qas;
            if (sidUp.EndsWith("D") && sidUp.StartsWith("S")) return EnvKind.Dev;
        }
        return EnvKind.Unknown;
    }
}
