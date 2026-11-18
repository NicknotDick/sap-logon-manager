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
