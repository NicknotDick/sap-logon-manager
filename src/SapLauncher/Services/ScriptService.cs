using System.Diagnostics;
using System.IO;
using SapLauncher.Models;

namespace SapLauncher.Services;

/// <summary>VBS 脚本库：存放于 %LOCALAPPDATA%\SapLauncher\scripts，登录后通过 cscript 执行。</summary>
public sealed class ScriptService
{
    private readonly ManifestData _manifest;

    public ScriptService(ManifestData manifest) => _manifest = manifest;

    public IReadOnlyList<ScriptDef> Scripts => _manifest.Scripts;

    public ScriptDef? Find(string id) => _manifest.Scripts.FirstOrDefault(s => s.Id == id);

    public string PathOf(ScriptDef def) => Path.Combine(AppPaths.Scripts, def.FileName);

    public void Refresh()
    {
        try
        {
            AppPaths.EnsureAll();
            var files = Directory.GetFiles(AppPaths.Scripts, "*.vbs");
            // 登记磁盘上新增的文件
            foreach (var f in files)
            {
                var name = Path.GetFileName(f);
                if (_manifest.Scripts.Any(s => string.Equals(s.FileName, name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                _manifest.Scripts.Add(new ScriptDef
                {
                    Name = Path.GetFileNameWithoutExtension(f),
                    FileName = name
                });
            }
            // 移除已删除的文件
            _manifest.Scripts.RemoveAll(s => !File.Exists(Path.Combine(AppPaths.Scripts, s.FileName)));
        }
        catch (Exception ex)
        {
            Log.Warn("刷新脚本库失败：" + ex.Message);
        }
    }

    public ScriptDef Create(string name, string content = "")
    {
        AppPaths.EnsureAll();
        var safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
        var fileName = safe + ".vbs";
        var full = Path.Combine(AppPaths.Scripts, fileName);
        int i = 1;
        while (File.Exists(full))
        {
            fileName = $"{safe}_{i++}.vbs";
            full = Path.Combine(AppPaths.Scripts, fileName);
        }
        File.WriteAllText(full, string.IsNullOrWhiteSpace(content) ? DefaultTemplate : content);
        var def = new ScriptDef { Name = name, FileName = fileName };
        _manifest.Scripts.Add(def);
        return def;
    }

    public ScriptDef Import(string sourcePath)
    {
        AppPaths.EnsureAll();
        var fileName = Path.GetFileName(sourcePath);
        var dest = Path.Combine(AppPaths.Scripts, fileName);
        int i = 1;
        while (File.Exists(dest))
        {
            fileName = Path.GetFileNameWithoutExtension(sourcePath) + $"_{i++}.vbs";
            dest = Path.Combine(AppPaths.Scripts, fileName);
        }
        File.Copy(sourcePath, dest);
        var def = new ScriptDef
        {
            Name = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName
        };
        _manifest.Scripts.Add(def);
        return def;
    }

    public string Read(ScriptDef def)
    {
        try
        {
            var p = PathOf(def);
            return File.Exists(p) ? File.ReadAllText(p) : "";
        }
        catch (Exception ex) { return "读取失败：" + ex.Message; }
    }

    public void Write(ScriptDef def, string content) => File.WriteAllText(PathOf(def), content);

    public void Remove(ScriptDef def)
    {
        try
        {
            var p = PathOf(def);
            if (File.Exists(p)) File.Delete(p);
        }
        catch (Exception ex)
        {
            Log.Warn("删除脚本文件失败：" + ex.Message);
        }
        _manifest.Scripts.Remove(def);
    }

    /// <summary>执行脚本。失败不影响登录本身（依赖服务端 sapgui/user_scripting）。</summary>
    public async Task<(bool ok, string output)> RunAsync(ScriptDef def)
    {
        try
        {
            var path = PathOf(def);
            if (!File.Exists(path)) return (false, "脚本文件不存在");

            var psi = new ProcessStartInfo("cscript.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("//nologo");
            psi.ArgumentList.Add("//B");
            psi.ArgumentList.Add(path);

            using var proc = Process.Start(psi);
            if (proc is null) return (false, "无法启动 cscript");
            var output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var error = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await proc.WaitForExitAsync().ConfigureAwait(false);

            var text = (output + error).Trim();
            var ok = proc.ExitCode == 0;
            Log.Info($"执行脚本 {def.Name}：退出码 {proc.ExitCode}");
            return (ok, string.IsNullOrWhiteSpace(text) ? (ok ? "执行完成" : "执行失败") : text);
        }
        catch (Exception ex)
        {
            Log.Error("执行脚本失败", ex);
            return (false, ex.Message);
        }
    }

    private const string DefaultTemplate = """
        ' SAP GUI Scripting 示例：打开事务码 SE38
        Dim SapGui, App, Conn, Session
        Set SapGui = GetObject("SAPGUI")
        Set App = SapGui.GetScriptingEngine
        If App.Children.Count = 0 Then
            WScript.Echo "未找到已连接的 SAP 会话"
            WScript.Quit 1
        End If
        Set Conn = App.Children(0)
        Set Session = Conn.Children(0)
        Session.StartTransaction "SE38"
        """;
}
