using System.Text.RegularExpressions;

namespace SapLauncher.Services;

public class CredentialCandidate
{
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string Client { get; set; } = "";
    public string Evidence { get; set; } = "";
    public int Score { get; set; }

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(User)) parts.Add("账号 " + User);
            if (!string.IsNullOrWhiteSpace(Password)) parts.Add("密码 " + new string('●', Math.Min(8, Password.Length)));
            if (!string.IsNullOrWhiteSpace(Client)) parts.Add("客户端 " + Client);
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// 从 SAP Logon 备注中识别可能的账号/密码/客户端。
/// 结果仅作为「待确认候选」，绝不自动入库。
/// </summary>
public static class MemoCredentialParser
{
    private static readonly Regex UserPwd = new(
        @"(?:用户|用户名|账号|帐号|user(?:name)?)\s*[:：=]?\s*([A-Za-z0-9_.\-]{2,24})[^\r\n]{0,16}?(?:密码|口令|password|passwd|pwd)\s*[:：=]?\s*([^\s，,；;、]{4,48})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PwdUser = new(
        @"(?:密码|口令|password|passwd|pwd)\s*[:：=]?\s*([^\s，,；;、]{4,48})[^\r\n]{0,16}?(?:用户|用户名|账号|帐号|user(?:name)?)\s*[:：=]?\s*([A-Za-z0-9_.\-]{2,24})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Client = new(
        @"(?:(?:客户端|集团|client|mandant)\s*[:：=]?\s*(\d{3}))|(?:(\d{3})\s*(?:客户端|client))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClientPwd = new(
        @"(?:^|[\s，,；;])(\d{3})\s*[:：]\s*([^\s，,；;、]{4,48})",
        RegexOptions.Compiled);

    public static List<CredentialCandidate> Parse(string? memo)
    {
        var list = new List<CredentialCandidate>();
        if (string.IsNullOrWhiteSpace(memo)) return list;

        var text = memo.Replace("\r\n", "\n");
        var clients = new List<string>();
        foreach (Match m in Client.Matches(text))
        {
            var c = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(c) && !clients.Contains(c)) clients.Add(c);
        }

        // 1) 显式「用户 + 密码」
        foreach (Match m in UserPwd.Matches(text))
        {
            list.Add(new CredentialCandidate
            {
                User = m.Groups[1].Value.Trim(),
                Password = m.Groups[2].Value.Trim(),
                Client = clients.FirstOrDefault() ?? "",
                Evidence = Snippet(text, m.Index),
                Score = 95
            });
        }

        // 2) 「密码 + 用户」倒序写法
        foreach (Match m in PwdUser.Matches(text))
        {
            list.Add(new CredentialCandidate
            {
                User = m.Groups[2].Value.Trim(),
                Password = m.Groups[1].Value.Trim(),
                Client = clients.FirstOrDefault() ?? "",
                Evidence = Snippet(text, m.Index),
                Score = 90
            });
        }

        // 3) 「客户端:密码」
        foreach (Match m in ClientPwd.Matches(text))
        {
            var pwd = m.Groups[2].Value.Trim();
            if (!LooksLikePassword(pwd)) continue;
            list.Add(new CredentialCandidate
            {
                Password = pwd,
                Client = m.Groups[1].Value,
                Evidence = Snippet(text, m.Index),
                Score = 80
            });
        }

        // 4) 独立成行的密码（无关键词标注）
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!LooksLikePassword(line)) continue;
            if (list.Any(c => c.Password == line)) continue;
            list.Add(new CredentialCandidate
            {
                Password = line,
                Client = clients.Count == 1 ? clients[0] : "",
                Evidence = "整行内容疑似密码",
                Score = clients.Count == 1 ? 60 : 50
            });
        }

        // 5) 只有账号
        if (list.Count == 0)
        {
            var m = Regex.Match(text, @"(?:用户|用户名|账号|帐号|user(?:name)?)\s*[:：=]?\s*([A-Za-z0-9_.\-]{2,24})",
                RegexOptions.IgnoreCase);
            if (m.Success)
            {
                list.Add(new CredentialCandidate
                {
                    User = m.Groups[1].Value,
                    Client = clients.FirstOrDefault() ?? "",
                    Evidence = "仅识别到账号",
                    Score = 35
                });
            }
        }

        return list
            .GroupBy(c => c.User + "|" + c.Password + "|" + c.Client)
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(c => c.Score)
            .Take(6)
            .ToList();
    }

    private static bool LooksLikePassword(string line)
    {
        if (line.Length < 5 || line.Length > 48) return false;
        if (line.Contains(' ') || line.Contains('\t')) return false;
        if (line.StartsWith("[") || line.StartsWith(";")) return false;
        if (Regex.IsMatch(line, @"^(https?|Router|router|客户端|用户|密码)", RegexOptions.IgnoreCase)) return false;
        if (Regex.IsMatch(line, @"^[-/]")) return false;
        bool hasLetter = line.Any(char.IsLetter);
        bool hasDigit = line.Any(char.IsDigit);
        bool hasSymbol = line.Any(ch => !char.IsLetterOrDigit(ch));
        // 中文整句排除
        if (line.Count(ch => ch > 0x4E00) > 2) return false;
        return hasLetter && (hasDigit || hasSymbol);
    }

    private static string Snippet(string text, int index)
    {
        var start = Math.Max(0, index - 10);
        var len = Math.Min(60, text.Length - start);
        return text.Substring(start, len).Replace("\n", " ").Trim();
    }
}
