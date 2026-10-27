using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SapLauncher.Interop;

/// <summary>Win32 互操作：DPAPI、代码页转换、窗口枚举、热键、深色标题栏。</summary>
internal static class NativeMethods
{
    // ---------------- DPAPI ----------------

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr,
        ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
        ref DATA_BLOB pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static byte[] DpapiProtect(byte[] data, byte[] entropy)
    {
        var inBlob = new DATA_BLOB();
        var entBlob = new DATA_BLOB();
        GCHandle hIn = default, hEnt = default;
        try
        {
            hIn = GCHandle.Alloc(data, GCHandleType.Pinned);
            hEnt = GCHandle.Alloc(entropy, GCHandleType.Pinned);
            inBlob.cbData = data.Length;
            inBlob.pbData = hIn.AddrOfPinnedObject();
            entBlob.cbData = entropy.Length;
            entBlob.pbData = hEnt.AddrOfPinnedObject();

            if (!CryptProtectData(ref inBlob, "SapLauncher", ref entBlob, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, out var outBlob))
                throw new InvalidOperationException("DPAPI 加密失败：" + Marshal.GetLastWin32Error());

            try
            {
                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }
        finally
        {
            if (hIn.IsAllocated) hIn.Free();
            if (hEnt.IsAllocated) hEnt.Free();
        }
    }

    public static byte[]? DpapiUnprotect(byte[] data, byte[] entropy)
    {
        var inBlob = new DATA_BLOB();
        var entBlob = new DATA_BLOB();
        GCHandle hIn = default, hEnt = default;
        try
        {
            hIn = GCHandle.Alloc(data, GCHandleType.Pinned);
            hEnt = GCHandle.Alloc(entropy, GCHandleType.Pinned);
            inBlob.cbData = data.Length;
            inBlob.pbData = hIn.AddrOfPinnedObject();
            entBlob.cbData = entropy.Length;
            entBlob.pbData = hEnt.AddrOfPinnedObject();

            if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entBlob, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, out var outBlob))
                return null;

            try
            {
                var result = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
                return result;
            }
            finally
            {
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }
        finally
        {
            if (hIn.IsAllocated) hIn.Free();
            if (hEnt.IsAllocated) hEnt.Free();
        }
    }

    // ---------------- 代码页（SAP .sap 文件使用 ANSI/CP936） ----------------

    private const uint CP_ACP = 0;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WideCharToMultiByte(uint codePage, uint dwFlags, string lpWideCharStr,
        int cchWideChar, byte[]? lpMultiByteStr, int cbMultiByte, IntPtr lpDefaultChar, IntPtr lpUsedDefaultChar);

    /// <summary>按系统 ANSI 代码页（简体中文环境为 936/GBK）编码，供 sapshcut 读取。</summary>
    public static byte[] ToAnsi(string text)
    {
        int len = WideCharToMultiByte(CP_ACP, 0, text, text.Length, null, 0, IntPtr.Zero, IntPtr.Zero);
        if (len <= 0) return Encoding.UTF8.GetBytes(text);
        var buffer = new byte[len];
        int written = WideCharToMultiByte(CP_ACP, 0, text, text.Length, buffer, len, IntPtr.Zero, IntPtr.Zero);
        if (written <= 0) return Encoding.UTF8.GetBytes(text);
        if (written != len) Array.Resize(ref buffer, written);
        return buffer;
    }

    // ---------------- 窗口枚举（检测 SAP GUI 会话窗口） ----------------

    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    public static int CountSapSessionWindows()
    {
        int count = 0;
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(256);
            if (GetClassName(h, sb, sb.Capacity) > 0 &&
                sb.ToString().Equals("SAP_FRONTEND_SESSION", StringComparison.OrdinalIgnoreCase))
                count++;
            return true;
        }, IntPtr.Zero);
        return count;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    /// <summary>当前可见的对话框标题（类 #32770）。用于诊断 SAP 到底弹了什么。</summary>
    public static List<string> GetVisibleDialogTitles()
    {
        var titles = new List<string>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(256);
            if (GetClassName(h, sb, sb.Capacity) > 0 && sb.ToString().Equals("#32770", StringComparison.Ordinal))
            {
                int len = GetWindowTextLength(h);
                var tb = new StringBuilder(len + 1);
                GetWindowText(h, tb, tb.Capacity);
                var t = tb.ToString().Trim();
                if (t.Length > 0) titles.Add(t);
            }
            return true;
        }, IntPtr.Zero);
        return titles;
    }

    /// <summary>枚举所有 SAP 会话窗口（句柄 + 标题）。标题中带 SID，如 "SAP PRD (1)"。</summary>
    public static List<(IntPtr handle, string title)> GetSessionWindows()
    {
        var list = new List<(IntPtr, string)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(256);
            if (GetClassName(h, sb, sb.Capacity) > 0 &&
                sb.ToString().Equals("SAP_FRONTEND_SESSION", StringComparison.OrdinalIgnoreCase))
            {
                int len = GetWindowTextLength(h);
                var tb = new StringBuilder(len + 1);
                GetWindowText(h, tb, tb.Capacity);
                list.Add((h, tb.ToString()));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// 是否可见 SAP 登录框（类 #32770，标题即系统名）。
    /// 用于区分「自动登录成功」与「SAP 弹出登录框要求手工输入」。
    /// </summary>
    public static bool IsSapLogonDialogVisible(string systemName)
    {
        if (string.IsNullOrWhiteSpace(systemName)) return false;
        var target = systemName.Trim();
        bool found = false;

        EnumWindows((h, _) =>
        {
            if (found || !IsWindowVisible(h)) return true;
            var sb = new StringBuilder(256);
            if (GetClassName(h, sb, sb.Capacity) > 0 &&
                sb.ToString().Equals("#32770", StringComparison.Ordinal))
            {
                int len = GetWindowTextLength(h);
                var tb = new StringBuilder(len + 1);
                GetWindowText(h, tb, tb.Capacity);
                if (string.Equals(tb.ToString().Trim(), target, StringComparison.CurrentCultureIgnoreCase))
                {
                    found = true;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    // ---------------- 深色标题栏 ----------------

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>按当前主题设置标题栏深浅（Windows 10 1809+ 支持，失败静默忽略）。</summary>
    public static void ApplyDarkTitleBar(IntPtr hwnd, bool dark)
    {
        try
        {
            int value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch { }
    }

    // ---------------- 系统空闲时长 ----------------

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    /// <summary>用户最后一次输入（键盘/鼠标）至今的秒数，用于闲置自动锁定。</summary>
    public static double GetSystemIdleSeconds()
    {
        try
        {
            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref lii)) return 0;
            uint tick = GetTickCount();
            // GetTickCount 约 49.7 天回绕，做一次差值校正
            long idle = tick >= lii.dwTime ? tick - lii.dwTime : (0xFFFFFFFFL - lii.dwTime) + tick;
            return idle / 1000.0;
        }
        catch { return 0; }
    }

    // ---------------- 全局热键 ----------------

    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_WIN = 0x8;
    public const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---------------- SAP 快捷方式登录弹框：可信性校验与回填 ----------------
    //
    // 实测（2026-10-27，SAP GUI 8.0）：
    //   标题 "SAP GUI 快捷方式 - 登录 (PRD, 800, ZH, )"，类 #32770
    //   控件 Edit id=1004 用户名 / Edit id=1005 密码 / Button id=1 &Logon / Button id=2 &Cancel
    //   窗口来源可信性校验（防钓鱼）、写入后读回校验、带诊断信息的填充入口。
    // 本段自带独立 P/Invoke 声明（Guard 前缀），不依赖也不占用文件中的其它同名成员。

    private const int GuardWmSetText = 0x000C;
    private const int GuardWmGetText = 0x000D;
    private const int GuardBmClick = 0x00F5;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private const int GuardUserEditId = 1004;
    private const int GuardPasswordEditId = 1005;
    private const int GuardLogonButtonId = 1;

    [DllImport("user32.dll", EntryPoint = "GetDlgItem")]
    private static extern IntPtr GuardGetDlgItem(IntPtr hDlg, int nIDDlgItem);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr GuardSendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern IntPtr GuardSendMessageRead(IntPtr hWnd, int msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr GuardSendMessagePtr(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder sb, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    /// <summary>所有可见的 #32770 窗口及其标题（登录弹框从里面筛）。</summary>
    public static List<(IntPtr handle, string title)> GetVisibleDialogWindows()
    {
        var list = new List<(IntPtr, string)>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(256);
            if (GetClassName(h, sb, sb.Capacity) > 0 && sb.ToString().Equals("#32770", StringComparison.Ordinal))
            {
                int len = GetWindowTextLength(h);
                var tb = new StringBuilder(len + 1);
                GetWindowText(h, tb, tb.Capacity);
                var t = tb.ToString().Trim();
                if (t.Length > 0) list.Add((h, t));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// 校验窗口所属进程确实是 SAP GUI（防钓鱼：绝不能把密码填给任意同名弹框）。
    /// 要求镜像名为 saplogon.exe / sapgui.exe，且位于 SAP GUI 安装目录下。
    /// </summary>
    public static bool IsTrustedSapWindow(IntPtr hWnd, string? sapGuiDir, out string imagePath, out string reason)
    {
        imagePath = "";
        reason = "";
        GetWindowThreadProcessId(hWnd, out var pid);
        if (pid <= 0)
        {
            reason = "取不到进程号";
            return false;
        }

        var hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc == IntPtr.Zero)
        {
            reason = "无法打开进程 " + pid;
            return false;
        }

        try
        {
            int size = 1024;
            var sb = new StringBuilder(size);
            if (!QueryFullProcessImageName(hProc, 0, sb, ref size))
            {
                reason = "取不到进程路径";
                return false;
            }
            imagePath = sb.ToString();
        }
        finally
        {
            CloseHandle(hProc);
        }

        var name = Path.GetFileName(imagePath);
        // sapshcut.exe 也要接受：.sap 是由它启动的，登录框的属主进程可能就是它本身。
        // 三者都在 SAP 安装目录下（下方还有目录校验），不放行任何目录外的仿冒窗口。
        if (!name.Equals("saplogon.exe", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("sapgui.exe", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("sapshcut.exe", StringComparison.OrdinalIgnoreCase))
        {
            reason = "进程不是 SAP：" + name;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(sapGuiDir))
        {
            try
            {
                var dir = Path.GetFullPath(sapGuiDir).TrimEnd('\\') + "\\";
                if (!Path.GetFullPath(imagePath).StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "进程不在 SAP 安装目录：" + imagePath;
                    return false;
                }
            }
            catch
            {
                reason = "路径规范化失败";
                return false;
            }
        }

        return true;
    }

    /// <summary>窗口是否具备登录弹框的结构特征（用户名/密码 Edit + 登录按钮），与界面语言无关。</summary>
    public static bool LooksLikeLogonDialog(IntPtr hDlg, out string detail)
    {
        detail = "";
        var user = GuardGetDlgItem(hDlg, GuardUserEditId);
        var pwd = GuardGetDlgItem(hDlg, GuardPasswordEditId);
        var btn = GuardGetDlgItem(hDlg, GuardLogonButtonId);
        if (pwd == IntPtr.Zero || btn == IntPtr.Zero)
        {
            detail = $"缺少控件（密码框={pwd != IntPtr.Zero} 登录按钮={btn != IntPtr.Zero}）";
            return false;
        }

        var cls = new StringBuilder(64);
        GetClassName(pwd, cls, cls.Capacity);
        if (!cls.ToString().Equals("Edit", StringComparison.OrdinalIgnoreCase))
        {
            detail = "1005 不是 Edit 控件：" + cls;
            return false;
        }
        return true;
    }

    /// <summary>读控件文本（跨进程用 WM_GETTEXT，GetWindowText 对他人窗口不可靠）。</summary>
    public static string GetControlText(IntPtr hCtrl)
    {
        var sb = new StringBuilder(512);
        GuardSendMessageRead(hCtrl, GuardWmGetText, (IntPtr)sb.Capacity, sb);
        return sb.ToString();
    }

    private static bool SetControlText(IntPtr hCtrl, string text)
    {
        GuardSendMessage(hCtrl, GuardWmSetText, IntPtr.Zero, text);
        if (GetControlText(hCtrl) == text) return true;

        // 兜底：部分自绘/子类化的输入框不接受 WM_SETTEXT，改用 UI Automation 的 Value 模式
        //（实测 SAP GUI 8.0 登录框的 Edit 暴露了 ValuePattern）
        return TrySetViaUia(hCtrl, text) && GetControlText(hCtrl) == text;
    }

    private static bool TrySetViaUia(IntPtr hCtrl, string text)
    {
        try
        {
            var element = System.Windows.Automation.AutomationElement.FromHandle(hCtrl);
            if (element is null) return false;
            if (!element.TryGetCurrentPattern(System.Windows.Automation.ValuePattern.Pattern, out var pattern))
                return false;
            ((System.Windows.Automation.ValuePattern)pattern).SetValue(text);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 回填账号密码并点击登录。返回 false 时 detail 说明卡在哪一步。
    /// 用户名仅在框内为空时填写，避免覆盖 SAP 已预填的内容。
    /// </summary>
    public static bool FillLogonDialog(IntPtr hDlg, string? user, string password, out string detail)
    {
        detail = "";
        if (!LooksLikeLogonDialog(hDlg, out detail)) return false;

        var userEdit = GuardGetDlgItem(hDlg, GuardUserEditId);
        if (userEdit != IntPtr.Zero && !string.IsNullOrWhiteSpace(user))
        {
            if (string.IsNullOrWhiteSpace(GetControlText(userEdit)))
            {
                if (!SetControlText(userEdit, user))
                {
                    detail = "用户名写入未生效";
                    return false;
                }
            }
        }

        var pwdEdit = GuardGetDlgItem(hDlg, GuardPasswordEditId);
        if (!SetControlText(pwdEdit, password))
        {
            detail = "密码写入未生效";
            return false;
        }

        var logonBtn = GuardGetDlgItem(hDlg, GuardLogonButtonId);
        GuardSendMessagePtr(logonBtn, GuardBmClick, IntPtr.Zero, IntPtr.Zero);
        detail = "已填入密码并点击登录";
        return true;
    }
}
