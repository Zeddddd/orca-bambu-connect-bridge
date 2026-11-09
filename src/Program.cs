using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var instance = new Mutex(false, @"Local\OrcaBambuConnectBridge"))
        {
            bool first;
            try { first = instance.WaitOne(0); }
            catch (AbandonedMutexException) { first = true; }
            if (!first)
            {
                MessageBox.Show("已在运行。请从屏幕右下角的托盘图标打开。", BridgeWindow.AppTitle);
                return;
            }
            try
            {
                Application.Run(new BridgeWindow());
            }
            finally
            {
                instance.ReleaseMutex();
            }
        }
    }
}

internal sealed class BridgeWindow : Form
{
    internal const string AppTitle = "Orca → Bambu Connect";
    private readonly string settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WatchFolder.txt");
    private readonly string connectSettingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ConnectPath.txt");
    private readonly Color accent = Color.FromArgb(0, 104, 87);
    private readonly TextBox folderPath;
    private readonly Label connectLabel;
    private readonly TextBox connectPath;
    private readonly Label state;
    private readonly Label activity;
    private readonly NotifyIcon tray;
    private readonly ContextMenuStrip menu = new ContextMenuStrip();
    private readonly ToolTip details = new ToolTip();
    private readonly Icon appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
    private FolderMonitor monitor;
    private string manualConnectPath;
    private bool exiting;

    internal BridgeWindow()
    {
        Text = AppTitle;
        Icon = appIcon;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Microsoft YaHei UI", 9F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.White;
        ClientSize = new Size(520, 367);

        var header = new Label {
            Text = "切片文件交接", Bounds = new Rectangle(24, 18, 470, 35),
            Font = new Font(Font.FontFamily, 17F, FontStyle.Bold)
        };
        var intro = new Label {
            Text = "把拓竹机型的单盘切片自动交给 Bambu Connect。",
            Bounds = new Rectangle(24, 57, 470, 24), ForeColor = Color.FromArgb(62, 75, 81)
        };
        var folderLabel = new Label { Text = "监视文件夹", Bounds = new Rectangle(24, 101, 300, 21) };
        folderPath = new TextBox {
            Bounds = new Rectangle(24, 127, 360, 27), ReadOnly = true,
            AccessibleName = "监视文件夹路径", BackColor = Color.WhiteSmoke
        };
        var choose = new Button { Text = "选择文件夹…", Bounds = new Rectangle(393, 124, 103, 31) };
        choose.Click += ChooseFolder;
        connectLabel = new Label { Text = "Bambu Connect", Bounds = new Rectangle(24, 174, 470, 21) };
        connectPath = new TextBox {
            Bounds = new Rectangle(24, 199, 278, 27), ReadOnly = true,
            AccessibleName = "Bambu Connect 程序路径", BackColor = Color.WhiteSmoke
        };
        var chooseConnect = new Button { Text = "指定程序…", Bounds = new Rectangle(310, 196, 91, 31) };
        chooseConnect.Click += ChooseConnect;
        var autoConnect = new Button { Text = "自动查找", Bounds = new Rectangle(405, 196, 91, 31) };
        autoConnect.Click += delegate { SetConnectPath(""); };
        state = new Label {
            Text = "● 尚未设置", Bounds = new Rectangle(24, 240, 470, 24), ForeColor = Color.DimGray,
            AccessibleName = "监视状态"
        };
        activity = new Label {
            Text = "请选择 OrcaSlicer 保存 .gcode.3mf 的文件夹。",
            Bounds = new Rectangle(24, 266, 472, 43), AutoEllipsis = true, AccessibleName = "最近活动"
        };
        var hide = new Button { Text = "收起到托盘", Bounds = new Rectangle(384, 320, 112, 32) };
        hide.Click += delegate { Hide(); };
        var hint = new Label {
            Text = "在托盘图标上右键，可退出应用。",
            Bounds = new Rectangle(24, 327, 355, 24), ForeColor = Color.DimGray
        };
        Controls.AddRange(new Control[] { header, intro, folderLabel, folderPath, choose,
            connectLabel, connectPath, chooseConnect, autoConnect, state, activity, hint, hide });

        menu.Items.Add("打开窗口", null, delegate { ShowWindow(); });
        menu.Items.Add("退出应用", null, delegate { exiting = true; Close(); });
        tray = new NotifyIcon { Icon = appIcon, Text = AppTitle, ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += delegate { ShowWindow(); };
    }

    protected override void OnShown(EventArgs args)
    {
        base.OnShown(args);
        try
        {
            if (File.Exists(connectSettingsFile))
                manualConnectPath = File.ReadAllText(connectSettingsFile).Trim();
        }
        catch (IOException) { ShowActivity("无法读取上次指定的程序位置，已使用自动查找。"); }
        catch (UnauthorizedAccessException) { ShowActivity("无法读取上次指定的程序位置，已使用自动查找。"); }
        ResolveConnect();
        if (!File.Exists(settingsFile))
            return;
        try
        {
            var previous = File.ReadAllText(settingsFile).Trim();
            folderPath.Text = previous;
            StartMonitoring(previous);
        }
        catch (Exception error)
        {
            ShowFolderProblem(error);
        }
    }

    private void ChooseConnect(object sender, EventArgs args)
    {
        using (var dialog = new PathPicker(selectProgram: true, initialPath: connectPath.Text))
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
                SetConnectPath(dialog.SelectedPath);
        }
    }

    private void SetConnectPath(string path)
    {
        manualConnectPath = path;
        ResolveConnect();
        try
        {
            File.WriteAllText(connectSettingsFile, path);
        }
        catch (IOException) { ShowActivity("本次已应用，但无法保存程序位置。"); }
        catch (UnauthorizedAccessException) { ShowActivity("本次已应用，但无法保存程序位置。"); }
    }

    private ConnectLauncher ResolveConnect()
    {
        var launcher = ConnectLauncher.Locate(manualConnectPath);
        if (launcher != null)
        {
            connectLabel.Text = "Bambu Connect · " + launcher.Source;
            connectPath.Text = launcher.Executable;
        }
        else
        {
            connectLabel.Text = String.IsNullOrWhiteSpace(manualConnectPath)
                ? "Bambu Connect · 未找到" : "Bambu Connect · 手动位置已失效";
            connectPath.Text = manualConnectPath ?? "";
        }
        details.SetToolTip(connectPath, connectPath.Text);
        return launcher;
    }

    private void OpenSliceInConnect(string path)
    {
        var launcher = ResolveConnect();
        if (launcher == null)
            throw new Win32Exception(String.IsNullOrWhiteSpace(manualConnectPath)
                ? "找不到 Bambu Connect，请点击「指定程序」。"
                : "手动指定的位置已失效，请重新指定或点击「自动查找」。");
        launcher.OpenSlice(path);
    }

    private void ChooseFolder(object sender, EventArgs args)
    {
        using (var dialog = new PathPicker(selectProgram: false, initialPath: folderPath.Text))
        {
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                StartMonitoring(dialog.SelectedPath);
            }
            catch (Exception error)
            {
                ShowFolderProblem(error);
                return;
            }
            try
            {
                File.WriteAllText(settingsFile, folderPath.Text);
            }
            catch (IOException) { ShowActivity("本次监视已启动，但无法保存路径。请将应用放在可写的文件夹。"); }
            catch (UnauthorizedAccessException) { ShowActivity("本次监视已启动，但无法保存路径。请将应用放在可写的文件夹。"); }
        }
    }

    private void StartMonitoring(string folder)
    {
        if (monitor == null || !monitor.Active || !String.Equals(monitor.Folder, folder, StringComparison.OrdinalIgnoreCase))
        {
            var next = new FolderMonitor(folder, this, OpenSliceInConnect, ShowActivity);
            if (monitor != null)
                monitor.Dispose();
            monitor = next;
        }
        folderPath.Text = monitor.Folder;
        details.SetToolTip(folderPath, folderPath.Text);
        ShowActivity("等待新切片。已有文件将在重新导出或覆盖后处理。");
    }

    private void ShowFolderProblem(Exception error)
    {
        if (!(error is ArgumentException || error is IOException || error is UnauthorizedAccessException ||
            error is System.ComponentModel.Win32Exception))
            throw error;
        ShowActivity("无法监视所选文件夹。请检查路径或重新选择。");
        details.SetToolTip(activity, activity.Text + Environment.NewLine + error.Message);
    }

    private void ShowActivity(string message)
    {
        bool active = monitor != null && monitor.Active;
        state.Text = active ? "● 正在监视" : "● 未监视";
        state.ForeColor = active ? accent : Color.DimGray;
        activity.Text = message;
        details.SetToolTip(activity, message);
    }

    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnResize(EventArgs args)
    {
        base.OnResize(args);
        if (WindowState == FormWindowState.Minimized)
            Hide();
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        if (!exiting && args.CloseReason == CloseReason.UserClosing)
        {
            args.Cancel = true;
            Hide();
        }
        base.OnFormClosing(args);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            if (monitor != null)
                monitor.Dispose();
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
            }
            menu.Dispose();
            details.Dispose();
            if (appIcon != null)
                appIcon.Dispose();
        }
        base.Dispose(disposing);
    }
}
