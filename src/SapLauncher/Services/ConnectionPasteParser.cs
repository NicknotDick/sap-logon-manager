using System.Text.RegularExpressions;
using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>
/// 从剪贴板文本解析 SAP 连接信息，支持常见的分享/复制格式（详见 <see cref="TryParse"/>）。
/// 纯函数、无 UI 依赖，便于自检回归。
/// 已知局限：仅按 IPv4 / 主机名设计，未处理 IPv6（冒号与 host:port 分隔符冲突）。
/// </summary>
public static class ConnectionPasteParser
{
    /// <summary>解析结果：可直接送入编辑器确认的草稿系统 + 人类可读摘要。</summary>
    public sealed record ParseResult(SapSystem System, string Summary);

    /// <summary>
    /// 支持的格式（一行一条，多行时取首个有效行）：
    ///  0) 本程序「复制连接信息」产出的分享文本（多行标签格式，见 <see cref="ParseShareText"/>），完整往返解析；
    ///  1) 斜杠连接串：<c>/H/host/S/3200</c>、经路由 <c>/H/router/H/host/S/3200</c>、
    ///     负载均衡 <c>/M/mshost/S/3600/G/组名</c>；可带 <c>conn=</c> 前缀。
    ///  2) 三段式：<c>SID host port</c>（空白/逗号/分号分隔）。
    ///  3) 两段式：<c>host port</c> 或 <c>SID host[:port]</c>。
    ///  4) 单段：<c>host[:port]</c>（端口缺省 3200）。
    /// </summary>
    public static ParseResult? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // 0) 「复制连接信息」的分享文本（多行标签格式）优先，保证 复制 → 粘贴 完整往返
        if (text.Contains("连接方式：", StringComparison.Ordinal) || text.Contains("【SAP 连接】", StringComparison.Ordinal))
        {
            var share = ParseShareText(text);
            if (share is not null) return share;
        }

        var line = text.Split('\n', '\r')
            .Select(l => l.Trim().Trim(';', '，', '"', '\'', ' ', '\t'))
            .FirstOrDefault(l => l.Length > 0);
        if (string.IsNullOrWhiteSpace(line)) return null;

        if (line.StartsWith("conn=", StringComparison.OrdinalIgnoreCase)) line = line[5..].Trim();
        if (line.Length < 3 || line.Length > 300) return null;

        // 1) 斜杠连接串
        if (line.Contains("/H/", StringComparison.Ordinal) || line.Contains("/M/", StringComparison.Ordinal))
            return ParseSlash(line);

        var parts = line.Split(new[] { ' ', '\t', ',', '，', ';', ' ' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 3) return null;

        // 三段：SID host port
        if (parts.Length == 3)
        {
            if (!SidLike(parts[0])) return null;
            var ep = ParseEndpoint(parts[1]);
            if (ep is null || !int.TryParse(parts[2], out var p3) || p3 is < 1 or > 65535) return null;
            return Build(parts[0].ToUpperInvariant(), ep.Value.host, p3, "", "", ConnKind.Direct, line);
        }

        // 两段：优先 host port；否则 SID host[:port]
        if (parts.Length == 2 && int.TryParse(parts[1], out var p2) && p2 is >= 1 and <= 65535)
        {
            var host = ParseEndpoint(parts[0]);
            if (host is null) return null;
            return Build(null, host.Value.host, p2, "", "", ConnKind.Direct, line);
        }
        if (parts.Length == 2 && SidLike(parts[0]))
        {
            var ep = ParseEndpoint(parts[1]);
            if (ep is null) return null;
            return Build(parts[0].ToUpperInvariant(), ep.Value.host, ep.Value.port, "", "", ConnKind.Direct, line);
        }

        // 单段：host[:port]
        if (parts.Length == 1)
        {
            var ep = ParseEndpoint(parts[0]);
            if (ep is null) return null;
            return Build(null, ep.Value.host, ep.Value.port, "", "", ConnKind.Direct, line);
        }

        return null;
    }

    /// <summary>
    /// 解析本程序「复制连接信息」（<see cref="ExportImportService.BuildShareText"/>）产出的分享文本：
    /// <code>
    /// 【SAP 连接】名称
    /// 系统 ID：SID
    /// 连接方式：直连 host:port | 经 Router → host:port（下一行 Router：xxx）| 消息服务器（ms）
    /// 账号：user            ← 不含密码，忽略
    /// 常用客户端：500
    /// 备注：xxx
    /// （密码请另行获取，此信息不含密码）
    /// </code>
    /// </summary>
    private static ParseResult? ParseShareText(string text)
    {
        string? name = null, sid = null, host = null, router = null, ms = null, memo = null, client = null;
        var port = 3200;
        ConnKind? kind = null;

        foreach (var rawLine in text.Split('\n', '\r'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("【SAP 连接】", StringComparison.Ordinal))
                name = line["【SAP 连接】".Length..].Trim();
            else if (TakeAfter(line, "系统 ID：", out var vSid)) sid = vSid;
            else if (line.StartsWith("连接方式：", StringComparison.Ordinal))
            {
                var rest = line["连接方式：".Length..].Trim();
                var mDirect = Regex.Match(rest, @"直连\s*(\S+?):(\d+)$");
                var mRouter = Regex.Match(rest, @"Router\s*→\s*(\S+?):(\d+)$");
                var mMsvc = Regex.Match(rest, @"消息服务器（([^）]+)）");
                if (mDirect.Success)
                {
                    kind = ConnKind.Direct; host = mDirect.Groups[1].Value;
                    port = int.TryParse(mDirect.Groups[2].Value, out var p) ? p : 3200;
                }
                else if (mRouter.Success)
                {
                    kind = ConnKind.Router; host = mRouter.Groups[1].Value;
                    port = int.TryParse(mRouter.Groups[2].Value, out var p) ? p : 3200;
                }
                else if (mMsvc.Success)
                {
                    kind = ConnKind.LoadBalanced; ms = mMsvc.Groups[1].Value; port = 3600;
                }
            }
            else if (TakeAfter(line, "Router：", out var vRouter)) router = vRouter;
            else if (TakeAfter(line, "常用客户端：", out var vClient)) client = vClient;
            else if (TakeAfter(line, "备注：", out var vMemo)) memo = vMemo;
            // 「账号：xxx」不含密码，不回填（避免误导为可登录）
        }

        if (kind is null) return null;

        var system = new SapSystem
        {
            Uuid = "local:" + Guid.NewGuid().ToString("N"),
            IsLocal = true,
            SortOrder = int.MaxValue,
            Env = EnvKind.Unknown,
            Kind = kind.Value,
            Name = string.IsNullOrWhiteSpace(name) ? (sid ?? (ms ?? host)) ?? "" : name!,
            Sid = sid ?? "",
            Host = host ?? "",
            Port = port,
            Router = router ?? "",
            MessageServer = ms ?? "",
            LastClient = client ?? "",
            Memo = memo ?? ""
        };

        var target = system.Kind switch
        {
            ConnKind.LoadBalanced => $"消息服务器 {system.MessageServer}:{system.Port}",
            ConnKind.Router => $"{system.Router} → {system.Host}:{system.Port}",
            _ => $"{system.Host}:{system.Port}"
        };
        return new ParseResult(system,
            (string.IsNullOrEmpty(system.Sid) ? "" : system.Sid + " ") + target);
    }

    private static bool TakeAfter(string line, string prefix, out string value)
    {
        if (line.StartsWith(prefix, StringComparison.Ordinal))
        {
            value = line[prefix.Length..].Trim();
            return value.Length > 0;
        }
        value = "";
        return false;
    }

    private static ParseResult? ParseSlash(string line)
    {
        var port = 0;
        var m = Regex.Match(line, @"/S/(\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var sp) && sp is >= 1 and <= 65535) port = sp;

        // /M/ → 负载均衡：连接信息在消息服务器，host 留空（编辑器中该字段此时不可用）
        var ms = Regex.Match(line, @"/M/([^/\s]+)");
        if (ms.Success)
        {
            if (!HostOk(ms.Groups[1].Value)) return null;
            var grp = Regex.Match(line, @"/G/([^/\s]+)");
            // SapSystem 无登录组字段，/G/ 值写入备注供用户搬到账号的登录组
            var memo = grp.Success ? "登录组: " + grp.Groups[1].Value : "";
            return Build(null, "", port == 0 ? 3600 : port, ms.Groups[1].Value, "", ConnKind.LoadBalanced, line, memo);
        }

        // /H/ 序列：1 段直连；≥2 段为「路由 → 目标」
        var hops = Regex.Matches(line, @"/H/([^/\s]+)")
            .Select(x => x.Groups[1].Value).ToList();
        if (hops.Count == 0) return null;
        if (hops.Any(h => !HostOk(h))) return null;

        return hops.Count == 1
            ? Build(null, hops[0], port == 0 ? 3200 : port, "", "", ConnKind.Direct, line)
            : Build(null, hops[^1], port == 0 ? 3200 : port, "", hops[0], ConnKind.Router, line);
    }

    private static ParseResult Build(string? sid, string host, int port,
        string messageServer, string router, ConnKind kind, string raw, string memo = "")
    {
        var system = new SapSystem
        {
            Uuid = "local:" + Guid.NewGuid().ToString("N"),
            IsLocal = true,
            SortOrder = int.MaxValue,
            Env = EnvKind.Unknown,
            Kind = kind,
            Sid = sid ?? "",
            Name = sid ?? (kind == ConnKind.LoadBalanced ? messageServer : host),
            Host = host,
            Port = port,
            MessageServer = messageServer,
            Router = router,
            Memo = memo
        };
        var target = kind switch
        {
            ConnKind.LoadBalanced => $"消息服务器 {messageServer}:{port}",
            ConnKind.Router => $"{router} → {host}:{port}",
            _ => $"{host}:{port}"
        };
        return new ParseResult(system,
            (string.IsNullOrEmpty(system.Sid) ? "" : system.Sid + " ") + target + $"（来源：{Truncate(raw)}）");
    }

    /// <summary>host:port 拆分（末段为数字端口时拆分，否则整体视为 host，端口取默认 3200）。</summary>
    private static (string host, int port)? ParseEndpoint(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Any(char.IsWhiteSpace)) return null;
        if (!Regex.IsMatch(s, @"^[A-Za-z0-9.\-:_]+$")) return null;

        var host = s;
        var port = 3200;
        var idx = s.LastIndexOf(':');
        if (idx > 0 && int.TryParse(s[(idx + 1)..], out var p) && p is >= 1 and <= 65535)
        {
            host = s[..idx];
            port = p;
        }
        if (host.Length == 0 || !HostOk(host) || !Regex.IsMatch(host, @"[.\d]"))
            return null; // 主机须含点或数字：排除把普通单词误判为主机（如 "hello world"）；
                         // 代价是 "localhost" 这类纯字母主机需用 host:port 或 /H/ 格式表达
        return (host, port);
    }

    private static bool HostOk(string h)
        => h.Length > 0 && h.Length <= 253 && Regex.IsMatch(h, @"^[A-Za-z0-9.\-_]+$");

    /// <summary>SID 形态：字母开头、2~5 位字母数字、不含点（与主机名区分）。</summary>
    private static bool SidLike(string s)
        => s.Length is >= 2 and <= 5 && Regex.IsMatch(s, @"^[A-Za-z][A-Za-z0-9]{1,4}$");

    private static string Truncate(string s) => s.Length <= 60 ? s : s[..57] + "…";
}
