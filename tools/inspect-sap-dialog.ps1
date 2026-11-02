# 探测 SAP 相关窗口的控件结构，判断是否可自动化
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$sig = @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class W32 {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, StringBuilder lp);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }

    public static string Cls(IntPtr h){ var sb=new StringBuilder(256); GetClassName(h,sb,256); return sb.ToString(); }
    public static string Txt(IntPtr h){ var sb=new StringBuilder(512); GetWindowText(h,sb,512); return sb.ToString(); }
    public static List<IntPtr> Tops(){
        var list=new List<IntPtr>();
        EnumWindows((h,l)=>{ list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
    public static List<IntPtr> Kids(IntPtr p){
        var list=new List<IntPtr>();
        EnumChildWindows(p,(h,l)=>{ list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }
}
'@
Add-Type -TypeDefinition $sig -ReferencedAssemblies System.Drawing

"=== 所有可见顶层窗口（类名 / 标题 / 进程） ==="
$tops = [W32]::Tops()
foreach ($h in $tops) {
    if (-not [W32]::IsWindowVisible($h)) { continue }
    $cls = [W32]::Cls($h); $txt = [W32]::Txt($h)
    $pid_ = 0; [void][W32]::GetWindowThreadProcessId($h, [ref]$pid_)
    $pn = try { (Get-Process -Id $pid_ -ErrorAction Stop).ProcessName } catch { '?' }
    if ($cls -match 'SAP|#32770' -or $pn -match 'sap') {
        "{0,-32} | {1,-45} | {2}" -f $cls, $txt, $pn
    }
}

"`n=== 逐个检查 SAP 顶层窗口的子控件 ==="
foreach ($h in $tops) {
    if (-not [W32]::IsWindowVisible($h)) { continue }
    $cls = [W32]::Cls($h)
    if ($cls -notmatch 'SAP|#32770') { continue }
    $txt = [W32]::Txt($h)
    "`n---------- [$cls] $txt ----------"
    $kids = [W32]::Kids($h)
    if ($kids.Count -eq 0) { "  (无 Win32 子窗口)" }
    foreach ($k in $kids) {
        "  child: class=[{0}] id={1} text=[{2}]" -f [W32]::Cls($k), [W32]::GetDlgCtrlID($k), ([W32]::Txt($k) -replace "`r?`n",' / ')
    }
    # UI Automation 树
    try {
        $ae = [System.Windows.Automation.AutomationElement]::FromHandle($h)
        "  --- UIA Name=[{0}] Class=[{1}] Type=[{2}] 子元素数={3}" -f $ae.Current.Name, $ae.Current.ClassName, $ae.Current.ControlType.ProgrammaticName, $ae.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition).Count
        $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
        $stack = New-Object System.Collections.Stack
        $stack.Push(@($ae,0))
        while ($stack.Count -gt 0) {
            $item = $stack.Pop(); $el = $item[0]; $d = $item[1]
            if ($d -gt 6) { continue }
            $c = $walker.GetFirstChild($el)
            while ($c -ne $null) {
                "    " + ('  ' * $d) + "- [{0}] Name=[{1}] Class=[{2}] AutoId=[{3}] Enabled={4}" -f $c.Current.ControlType.ProgrammaticName, $c.Current.Name, $c.Current.ClassName, $c.Current.AutomationId, $c.Current.IsEnabled
                $stack.Push(@($c, $d+1))
                $c = $walker.GetNextSibling($c)
            }
        }
    } catch { "  UIA 读取失败: " + $_.Exception.Message }
}
