using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SapLauncher.Views;

/// <summary>
/// 带「小眼睛」的密码框：<b>按住眼睛显示明文，松开即隐藏</b>（长按预览）。
/// 密码值通过 <see cref="Password"/> 读写，内部在 PasswordBox / TextBox 之间同步。
/// </summary>
public partial class PasswordField : UserControl
{
    private bool _showing;

    public PasswordField()
    {
        InitializeComponent();

        // 按住显示、松开隐藏（长按预览）
        EyeButton.PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left) ShowPlain();
        };
        EyeButton.PreviewMouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left) HidePlain();
        };
        // 拖出按钮 / 丢失鼠标捕获也要隐藏，避免明文残留
        EyeButton.MouseLeave += (_, _) => HidePlain();
        EyeButton.LostMouseCapture += (_, _) => HidePlain();
        // 键盘回车/空格触发的 Click 不保留明文
        EyeButton.Click += (_, _) => HidePlain();
    }

    /// <summary>当前密码（明文显示中则取明文框内容，保证用户编辑不丢失）。</summary>
    public string Password
    {
        get => _showing ? InnerPlain.Text : InnerBox.Password;
        set
        {
            var v = value ?? "";
            InnerBox.Password = v;
            InnerPlain.Text = v;
            HideInstant();
        }
    }

    public void Clear() => Password = "";

    /// <summary>把输入焦点放到当前可见的输入框（隐藏状态时聚焦密码框）。</summary>
    public void FocusInput()
    {
        if (_showing) InnerPlain.Focus();
        else InnerBox.Focus();
    }

    private void ShowPlain()
    {
        if (_showing) return;
        InnerPlain.Text = InnerBox.Password;
        InnerPlain.Visibility = Visibility.Visible;
        InnerBox.Visibility = Visibility.Collapsed;
        _showing = true;
    }

    private void HidePlain()
    {
        if (!_showing) return;
        InnerBox.Password = InnerPlain.Text;
        InnerPlain.Visibility = Visibility.Collapsed;
        InnerBox.Visibility = Visibility.Visible;
        _showing = false;
    }

    private void HideInstant()
    {
        InnerPlain.Visibility = Visibility.Collapsed;
        InnerBox.Visibility = Visibility.Visible;
        _showing = false;
    }
}
