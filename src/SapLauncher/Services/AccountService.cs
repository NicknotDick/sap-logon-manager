using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>某系统下按客户端分组的账号视图（供右键菜单与自检使用）。</summary>
public sealed class ClientAccountGroup
{
    public string Client { get; set; } = "";
    public List<Credential> Accounts { get; set; } = new();
}

/// <summary>
/// 多账号规则：同一「系统 + 客户端」的账号数量不设上限；
/// 首个账号自动成为默认账号，切换默认时旧默认取消，删除默认后自动提升下一个。
/// 每个客户端各自独立维护一套默认账号，互不干扰。
/// </summary>
public static class AccountService
{
    /// <summary>
    /// 右键菜单单条凭据的简要文本：「账号（姓名） · 语言」。姓名为空时省略括号，
    /// 语言为空按 ZH。客户端号由 FormatEntryLine 补在前面。抽成纯函数便于自检断言。
    /// </summary>
    public static string FormatAccountBrief(Credential c)
    {
        var who = (c.User ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(c.DisplayName)) who += "（" + c.DisplayName.Trim() + "）";
        var lang = string.IsNullOrWhiteSpace(c.Language) ? "ZH" : c.Language.Trim();
        return string.IsNullOrEmpty(who) ? lang : who + " · " + lang;
    }

    /// <summary>
    /// 右键菜单整行文本：「客户端 · 账号（姓名） · 语言」。客户端仅显示数字，
    /// 不显示"客户端"字样，也不附加任何默认标记文字（默认项由带 Enter 字样的独立项标识）。
    /// 默认登录项与普通项统一使用此格式。
    /// </summary>
    public static string FormatEntryLine(Credential c)
    {
        var client = string.IsNullOrWhiteSpace(c.Client) ? "—" : c.Client.Trim();
        return client + " · " + FormatAccountBrief(c);
    }

    /// <summary>
    /// 右键账号项显示文本（不含登录语言）：「客户端 · 账号（姓名）」。
    /// 语言改由该项的二级子菜单（ZH / EN）选择，避免主项文字过长。
    /// </summary>
    public static string FormatEntryLineNoLang(Credential c)
    {
        var who = (c.User ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(c.DisplayName)) who += "（" + c.DisplayName.Trim() + "）";
        var client = string.IsNullOrWhiteSpace(c.Client) ? "—" : c.Client.Trim();
        return string.IsNullOrEmpty(who) ? client : client + " · " + who;
    }

    /// <summary>右键语言子菜单的候选（与编辑器登录语言下拉一致）。</summary>
    public static readonly string[] LoginLanguages = { "ZH", "EN" };

    /// <summary>
    /// 两条凭据是否指向同一账号（同系统 + 同客户端 + 同账号），用于右键菜单去重：
    /// 默认登录项已经显示的那条，平铺列表里不再重复显示。
    /// </summary>
    public static bool IsSameAccount(Credential a, Credential b)
        => string.Equals(a.SystemUuid, b.SystemUuid, StringComparison.Ordinal) &&
           string.Equals(a.Client, b.Client, StringComparison.Ordinal) &&
           string.Equals(a.User, b.User, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 登录成功后把本次客户端记入「登录过的客户端」列表（去重、忽略大小写、
    /// 客户端号为空则忽略）。返回是否发生了新增。客户端号按数值升序排列便于展示。
    /// </summary>
    public static bool MarkLoggedInClient(SapSystem system, string? client)
    {
        if (string.IsNullOrWhiteSpace(client)) return false;
        var c = client.Trim();
        system.LoggedInClients ??= new List<string>();
        if (system.LoggedInClients.Any(x => string.Equals(x, c, StringComparison.OrdinalIgnoreCase)))
            return false;
        system.LoggedInClients.Add(c);
        SortClients(system.LoggedInClients);
        return true;
    }

    /// <summary>客户端号按数值升序（非数字排后），供列表展示统一使用。</summary>
    public static void SortClients(List<string> clients)
        => clients.Sort((a, b) =>
        {
            var an = int.TryParse(a, out var x);
            var bn = int.TryParse(b, out var y);
            if (an && bn && x != y) return x.CompareTo(y);
            if (an != bn) return an ? -1 : 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>某客户端已保存的账号数（仅用于展示，不再作为限制条件）。</summary>
    public static int Count(VaultService vault, string uuid, string client)
        => vault.Items.Count(c => c.SystemUuid == uuid &&
                                  string.Equals(c.Client, client, StringComparison.Ordinal));

    /// <summary>
    /// 取某系统的全部凭据，按客户端分组（纯函数，便于自检）。
    /// 客户端按数值升序排列（非数字排最后），组内默认账号优先、其余按最近更新降序。
    /// </summary>
    public static List<ClientAccountGroup> GroupByClient(IEnumerable<Credential> items, string uuid)
        => items.Where(c => string.Equals(c.SystemUuid, uuid, StringComparison.Ordinal))
            .GroupBy(c => c.Client ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(g => new ClientAccountGroup
            {
                Client = g.Key,
                Accounts = g.OrderByDescending(c => c.IsDefault)
                            .ThenByDescending(c => c.UpdatedAt)
                            .ToList()
            })
            .OrderBy(g => int.TryParse(g.Client, out var n) ? n : int.MaxValue)
            .ThenBy(g => g.Client, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// 保存账号。账号数量不设上限；新账号若为该客户端第一个，自动设为默认。
    /// 已存在同名的则覆盖（不占用新名额）。
    /// </summary>
    /// <param name="persist">false 时只改内存不写盘，用于批量保存后统一落盘一次。</param>
    public static bool Save(VaultService vault, Credential credential, out string error, bool persist = true)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(credential.User))
        {
            error = "账号不能为空";
            return false;
        }

        bool exists = vault.Items.Any(c => c.SystemUuid == credential.SystemUuid &&
                                           string.Equals(c.Client, credential.Client, StringComparison.Ordinal) &&
                                           string.Equals(c.User, credential.User, StringComparison.OrdinalIgnoreCase));

        if (!exists)
        {
            // 首个账号自动成为默认（仅同客户端内生效）
            if (Count(vault, credential.SystemUuid, credential.Client) == 0) credential.IsDefault = true;
        }

        if (credential.IsDefault) ClearOtherDefaults(vault, credential, persist);
        vault.Upsert(credential, persist);
        return true;
    }

    /// <summary>把指定账号设为默认（同客户端的其它账号取消默认）。</summary>
    public static void SetDefault(VaultService vault, string uuid, string client, string user, bool persist = true)
    {
        var accounts = vault.GetAll(uuid, client);
        foreach (var a in accounts) a.IsDefault = string.Equals(a.User, user, StringComparison.OrdinalIgnoreCase);
        foreach (var a in accounts) vault.Upsert(a, persist);
    }

    /// <summary>
    /// 把来源条目的非空信息合并进 keeper（姓名/密码/语言/Tcode/登录组/记住/默认）。
    /// 供「删除时保留带姓名条目」与工作副本去重共用，避免两处逻辑漂移。
    /// </summary>
    public static void MergeInto(Credential keeper, Credential src)
    {
        if (ReferenceEquals(keeper, src)) return;
        if (string.IsNullOrWhiteSpace(keeper.DisplayName) && !string.IsNullOrWhiteSpace(src.DisplayName))
            keeper.DisplayName = src.DisplayName;
        if (string.IsNullOrEmpty(keeper.Password) && !string.IsNullOrEmpty(src.Password))
            keeper.Password = src.Password;
        if (string.IsNullOrWhiteSpace(keeper.Language) && !string.IsNullOrWhiteSpace(src.Language))
            keeper.Language = src.Language;
        if (string.IsNullOrWhiteSpace(keeper.Tcode) && !string.IsNullOrWhiteSpace(src.Tcode))
            keeper.Tcode = src.Tcode;
        if (string.IsNullOrWhiteSpace(keeper.LogonGroup) && !string.IsNullOrWhiteSpace(src.LogonGroup))
            keeper.LogonGroup = src.LogonGroup;
        keeper.Remember |= src.Remember;
        keeper.IsDefault |= src.IsDefault;
    }

    /// <summary>
    /// 删除「客户端+账号」分组后决定保留哪条：候选（已排除用户选中的那条）中
    /// 若存在带姓名的条目，取信息最全的一条，把其余候选的非空信息合并进去后返回；
    /// 若都不带姓名，返回 null，表示整体删除该账号。
    /// </summary>
    public static Credential? PickDeletionKeeper(IEnumerable<Credential> candidates)
    {
        var list = candidates.Where(c => c is not null).ToList();
        var named = list.Where(c => !string.IsNullOrWhiteSpace(c.DisplayName)).ToList();
        if (named.Count == 0) return null;
        var keeper = named.OrderByDescending(c => c.Password?.Length ?? 0)
                          .ThenByDescending(c => c.UpdatedAt)
                          .First();
        foreach (var c in list) MergeInto(keeper, c);
        return keeper;
    }

    /// <summary>
    /// 删除账号。若删的是默认账号，自动把剩余账号中最近更新的提升为默认。
    /// </summary>
    public static void Delete(VaultService vault, string uuid, string client, string user, bool persist = true)
    {
        var wasDefault = vault.Get(uuid, client)?.User?.Equals(user, StringComparison.OrdinalIgnoreCase) == true;
        vault.RemoveAccount(uuid, client, user, persist);

        if (wasDefault)
        {
            var next = vault.GetAll(uuid, client).FirstOrDefault();
            if (next is not null)
            {
                next.IsDefault = true;
                vault.Upsert(next, persist);
            }
        }
    }

    private static void ClearOtherDefaults(VaultService vault, Credential keep, bool persist)
    {
        // 必须先物化：Upsert 会改写 _items，边遍历边改会抛「集合已修改」
        var others = vault.Items
            .Where(c => c.SystemUuid == keep.SystemUuid &&
                        string.Equals(c.Client, keep.Client, StringComparison.Ordinal) &&
                        !string.Equals(c.User, keep.User, StringComparison.OrdinalIgnoreCase) &&
                        c.IsDefault)
            .ToList();

        foreach (var other in others)
        {
            other.IsDefault = false;
            vault.Upsert(other, persist);
        }
    }
}
