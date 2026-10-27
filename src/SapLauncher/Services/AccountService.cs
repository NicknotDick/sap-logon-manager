using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>
/// 多账号规则：同一「系统 + 客户端」最多保存 8 个账号；
/// 首个账号自动成为默认账号，切换默认时旧默认取消，删除默认后自动提升下一个。
/// </summary>
public static class AccountService
{
    public const int MaxAccountsPerClient = 8;

    /// <summary>某客户端已保存的账号数。</summary>
    public static int Count(VaultService vault, string uuid, string client)
        => vault.Items.Count(c => c.SystemUuid == uuid &&
                                  string.Equals(c.Client, client, StringComparison.Ordinal));

    /// <summary>是否还能新增账号。</summary>
    public static bool CanAdd(VaultService vault, string uuid, string client, out string error)
    {
        var count = Count(vault, uuid, client);
        if (count >= MaxAccountsPerClient)
        {
            error = $"该客户端最多保存 {MaxAccountsPerClient} 个账号，请先删除不再使用的";
            return false;
        }
        error = "";
        return true;
    }

    /// <summary>
    /// 保存账号。新账号若为该客户端第一个，自动设为默认。
    /// 已存在同名的则覆盖（不占用新名额）。
    /// </summary>
    public static bool Save(VaultService vault, Credential credential, out string error)
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
            if (!CanAdd(vault, credential.SystemUuid, credential.Client, out error)) return false;
            // 首个账号自动成为默认
            if (Count(vault, credential.SystemUuid, credential.Client) == 0) credential.IsDefault = true;
        }

        if (credential.IsDefault) ClearOtherDefaults(vault, credential);
        vault.Upsert(credential);
        return true;
    }

    /// <summary>把指定账号设为默认（同客户端的其它账号取消默认）。</summary>
    public static void SetDefault(VaultService vault, string uuid, string client, string user)
    {
        var accounts = vault.GetAll(uuid, client);
        foreach (var a in accounts) a.IsDefault = string.Equals(a.User, user, StringComparison.OrdinalIgnoreCase);
        foreach (var a in accounts) vault.Upsert(a);
    }

    /// <summary>
    /// 删除账号。若删的是默认账号，自动把剩余账号中最近更新的提升为默认。
    /// </summary>
    public static void Delete(VaultService vault, string uuid, string client, string user)
    {
        var wasDefault = vault.Get(uuid, client)?.User?.Equals(user, StringComparison.OrdinalIgnoreCase) == true;
        vault.RemoveAccount(uuid, client, user);

        if (wasDefault)
        {
            var next = vault.GetAll(uuid, client).FirstOrDefault();
            if (next is not null)
            {
                next.IsDefault = true;
                vault.Upsert(next);
            }
        }
    }

    private static void ClearOtherDefaults(VaultService vault, Credential keep)
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
            vault.Upsert(other);
        }
    }
}
