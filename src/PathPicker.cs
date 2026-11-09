using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class PathPicker : Form
{
    private readonly bool selectProgram;
    private readonly TextBox pathInput;
    private readonly Label errorText;
    internal string SelectedPath { get; private set; }

    internal PathPicker(bool selectProgram, string initialPath)
    {
        this.selectProgram = selectProgram;
        Text = selectProgram ? "指定 Bambu Connect 程序" : "选择监视文件夹";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Microsoft YaHei UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ShowInTaskbar = false;
        MinimizeBox = MaximizeBox = false;
        BackColor = Color.White;
        ClientSize = new Size(590, 205);

        var title = new Label {
            Text = selectProgram ? "Bambu Connect.exe 的完整路径" : "OrcaSlicer 导出文件夹的完整路径",
            Bounds = new Rectangle(20, 17, 550, 22)
        };
        pathInput = new TextBox {
            Text = initialPath ?? "", Bounds = new Rectangle(20, 44, 410, 40),
            Multiline = true, AcceptsReturn = false, WordWrap = false,
            ScrollBars = ScrollBars.Horizontal, AccessibleName = title.Text
        };
        var paste = new Button { Text = "粘贴", Bounds = new Rectangle(438, 42, 61, 32) };
        paste.Click += PastePath;
        var browse = new Button { Text = "浏览…", Bounds = new Rectangle(505, 42, 65, 32) };
        browse.Click += Browse;
        var hint = new Label {
            Text = "可直接粘贴资源管理器「复制文件地址」得到的路径，带引号也可以。",
            ForeColor = Color.DimGray, Bounds = new Rectangle(20, 92, 550, 24)
        };
        errorText = new Label {
            ForeColor = Color.FromArgb(155, 38, 38), Bounds = new Rectangle(20, 121, 550, 26),
            AccessibleName = "路径提示"
        };
        pathInput.TextChanged += delegate { errorText.Text = ""; };
        var confirm = new Button { Text = "确定", Bounds = new Rectangle(414, 154, 75, 32) };
        confirm.Click += Confirm;
        var cancel = new Button { Text = "取消", Bounds = new Rectangle(495, 154, 75, 32), DialogResult = DialogResult.Cancel };
        Controls.AddRange(new Control[] { title, pathInput, paste, browse, hint, errorText, confirm, cancel });
        AcceptButton = confirm;
        CancelButton = cancel;
        ActiveControl = pathInput;
        Shown += delegate { pathInput.SelectAll(); };
    }

    private void PastePath(object sender, EventArgs args)
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                errorText.Text = "剪贴板里没有可粘贴的文本。";
                return;
            }
            pathInput.Text = Clipboard.GetText();
            pathInput.Focus();
            pathInput.SelectionStart = pathInput.TextLength;
            pathInput.ScrollToCaret();
        }
        catch (ExternalException)
        {
            errorText.Text = "暂时无法读取剪贴板，请再试一次。";
        }
    }

    private void Browse(object sender, EventArgs args)
    {
        string current;
        GetPath(pathInput.Text, out current);
        if (selectProgram)
        {
            using (var browser = new OpenFileDialog {
                Title = "浏览 Bambu Connect 程序",
                Filter = "Windows 程序 (*.exe)|*.exe",
                FileName = "Bambu Connect.exe",
                CheckFileExists = true,
                RestoreDirectory = true
            })
            {
                if (File.Exists(current))
                {
                    browser.InitialDirectory = Path.GetDirectoryName(current);
                    browser.FileName = Path.GetFileName(current);
                }
                else if (Directory.Exists(current))
                    browser.InitialDirectory = current;
                if (browser.ShowDialog(this) == DialogResult.OK)
                    pathInput.Text = browser.FileName;
            }
        }
        else
        {
            using (var browser = new FolderBrowserDialog {
                Description = "浏览 OrcaSlicer 导出文件夹",
                SelectedPath = Directory.Exists(current) ? current : "",
                ShowNewFolderButton = true
            })
            {
                if (browser.ShowDialog(this) == DialogResult.OK)
                    pathInput.Text = browser.SelectedPath;
            }
        }
    }

    private void Confirm(object sender, EventArgs args)
    {
        string path;
        string problem = GetPath(pathInput.Text, out path);
        if (problem == null)
        {
            if (selectProgram)
            {
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                    problem = "找不到这个 .exe 文件，请检查粘贴的路径。";
            }
            else if (!Directory.Exists(path))
                problem = "找不到这个文件夹，请检查粘贴的路径。";
        }
        if (problem != null)
        {
            errorText.Text = problem;
            pathInput.Focus();
            return;
        }
        SelectedPath = path;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static string GetPath(string input, out string path)
    {
        path = null;
        string text = input.Trim();
        if (text.Length >= 2 && text[0] == '"' && text[text.Length - 1] == '"')
            text = text.Substring(1, text.Length - 2);
        if (text.Length == 0)
            return "请粘贴一条路径，或点击「浏览」。";
        if (text.IndexOfAny(new[] { '\r', '\n', '"' }) >= 0)
            return "请一次只粘贴一条完整路径。";
        try
        {
            string root = Path.GetPathRoot(text);
            if (String.IsNullOrEmpty(root) || root.Length <= 1 || root.EndsWith(":", StringComparison.Ordinal))
                return "请输入带盘符的完整路径，或以两个反斜杠开头的网络路径。";
            path = Path.GetFullPath(text);
            return null;
        }
        catch (PathTooLongException) { return "这条路径超出应用支持的长度。"; }
        catch (ArgumentException) { return "这条路径的格式有误，请检查。"; }
        catch (NotSupportedException) { return "这条路径的格式有误，请检查。"; }
    }
}
