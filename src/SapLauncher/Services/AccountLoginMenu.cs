using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>
/// 账号登录菜单共享构建器：主窗口右键菜单与快速搜索窗口右键菜单共用同一套交互。
/// 账号项显示「客户端 · 账号（姓名）」（不含语言）；直接点文字按账号配置语言登录，
/// 鼠标悬停到文字右方命中区展开 ZH / EN 二级菜单，点选后按所选语言登录。
/// </summary>
public static class AccountLoginMenu
{
    /// <summary>模板未就绪时语言命中区的回退宽度（DIP）。</summary>
    private const double ArrowZoneWidth = 140;

    /// <summary>登录委托：languageOverride 为 null 表示用账号保存的语言。</summary>
    public delegate Task LoginHandler(SapSystem system, Credential account, string? languageOverride);

    /// <summary>
    /// 判断横向位置是否落在语言菜单命中区：账号文字右方的整个区域。
    /// 通过 MenuItem 模板的 HeaderPresenter 实际位置计算；模板未应用时回退到右侧固定宽度。
    /// </summary>
    public static bool IsInLanguageZone(MenuItem item, double x)
    {
        double start = item.ActualWidth - ArrowZoneWidth;
        if (item.Template?.FindName("HeaderPresenter", item) is FrameworkElement fe)
        {
            try
            {
                start = fe.TransformToAncestor(item).Transform(new Point(0, 0)).X + fe.ActualWidth;
            }
            catch { /* 视觉树未就绪，用回退值 */ }
        }
        return x >= start;
    }

    /// <summary>
    /// 给账号菜单项挂语言二级菜单，并实现「直接点文字按配置语言登录」。
    /// WPF 中带子项的 MenuItem 属 Header 角色，按下任意位置都会展开子菜单而不触发 Click，
    /// 故在 PreviewMouseLeftButtonDown（隧道阶段）拦截；点在文字右方命中区则不拦截，交给 WPF 展开子菜单。
    /// <paramref name="systemGetter"/> 在事件触发时读取当前系统，避免闭包捕获过期对象。
    /// <paramref name="hooked"/> 防止同一 MenuItem（主窗口默认项常驻）被重复挂接事件。
    /// </summary>
    public static void AttachLanguageMenu(MenuItem item,
        Func<SapSystem?> systemGetter,
        Func<Credential?> accountGetter,
        LoginHandler login,
        Action closeMenu,
        ref bool hooked)
    {
        foreach (var lang in AccountService.LoginLanguages)
        {
            var langItem = new MenuItem { Header = lang };
            langItem.Click += async (_, _) =>
            {
                var a = accountGetter();
                var s = systemGetter();
                if (a is not null && s is not null) await login(s, a, lang);
            };
            item.Items.Add(langItem);
        }

        if (hooked) return;
        hooked = true;

        // 悬停展开校验：WPF 对 Header 角色菜单项在任意位置悬停都会自动展开子菜单，
        // 这里在 SubmenuOpened 校验鼠标横向位置，不在命中区则收回（Popup 尚未渲染，无闪烁）。
        item.SubmenuOpened += (_, _) =>
        {
            var pos = Mouse.GetPosition(item);
            if (item.ActualWidth <= 0 || !IsInLanguageZone(item, pos.X))
                item.IsSubmenuOpen = false;
        };

        item.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.Handled) return;
            var pos = e.GetPosition(item);
            if (item.ActualWidth > 0 && IsInLanguageZone(item, pos.X)) return; // 命中区：让子菜单展开
            var a = accountGetter();
            var s = systemGetter();
            if (a is null || s is null) return; // 无账号时保留菜单项自身 Click 行为
            e.Handled = true; // 阻止 Header 角色在按下阶段展开子菜单
            closeMenu();
            _ = login(s, a, null);
        };
    }

    /// <summary>
    /// 构建「除默认账号外」的账号菜单项，按客户端数值升序、组内默认优先。
    /// 与默认账号相同的条目跳过，避免同一账号出现两次。
    /// 纯函数：只读传入的凭据集合，不访问 Vault，便于自检。
    /// </summary>
    public static List<MenuItem> BuildAdditionalAccountItems(
        IEnumerable<Credential> accounts,
        string systemUuid,
        Credential? defaultAccount,
        Func<SapSystem?> systemGetter,
        LoginHandler login,
        Action closeMenu)
    {
        var result = new List<MenuItem>();
        var groups = AccountService.GroupByClient(accounts, systemUuid);
        foreach (var group in groups)
        {
            foreach (var account in group.Accounts)
            {
                if (defaultAccount is not null && AccountService.IsSameAccount(account, defaultAccount)) continue;
                var item = new MenuItem { Header = AccountService.FormatEntryLineNoLang(account) };
                var captured = account;
                var hooked = false;
                AttachLanguageMenu(item, systemGetter, () => captured, login, closeMenu, ref hooked);
                result.Add(item);
            }
        }
        return result;
    }
}
