using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class Verify
{
    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Pump(int milliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void Wait(Func<bool> condition, string message)
    {
        for (int i = 0; i < 100 && !condition(); i++)
            Pump(100);
        Assert(condition(), message);
    }

    private static byte[] MakeArchive(string model, string code, bool hasGcode)
    {
        using (var data = new MemoryStream())
        {
            using (var zip = new ZipArchive(data, ZipArchiveMode.Create, true))
            {
                if (model != null)
                    Write(zip, "Metadata/project_settings.config", "{\"printer_model\":\"" + model + "\"}");
                if (code != null)
                    Write(zip, "Metadata/slice_info.config",
                        "<config><plate><metadata key='printer_model_id' value='" + code + "'/></plate></config>");
                if (hasGcode)
                    Write(zip, "Metadata/plate_1.gcode", "; test data only");
            }
            return data.ToArray();
        }
    }

    private static void Write(ZipArchive zip, string path, string value)
    {
        using (var writer = new StreamWriter(zip.CreateEntry(path).Open()))
            writer.Write(value);
    }

    private static T Field<T>(object target, string name) where T : class
    {
        return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    private static void ChoosePath(BridgeWindow window, string buttonText, string selected, string screenshot)
    {
        Exception failure = null;
        using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
        {
            timer.Tick += delegate {
                var picker = Application.OpenForms.OfType<PathPicker>().SingleOrDefault();
                if (picker == null)
                    return;
                timer.Stop();
                try
                {
                    var input = Field<TextBox>(picker, "pathInput");
                    var error = Field<Label>(picker, "errorText");
                    Assert(picker.Controls.OfType<Button>().Any(b => b.Text == "粘贴"), "Paste button missing");
                    input.Text = selected + Environment.NewLine + selected;
                    picker.AcceptButton.PerformClick();
                    Assert(picker.Visible && error.Text.Contains("一条"), "Multiline paste was not rejected");
                    input.Text = "relative (1)";
                    picker.AcceptButton.PerformClick();
                    Assert(picker.Visible && error.Text.Contains("完整路径"), "Relative path was not rejected");
                    input.Text = " \"" + selected + "\" ";
                    using (var bitmap = new Bitmap(picker.Width, picker.Height))
                    {
                        picker.DrawToBitmap(bitmap, new Rectangle(Point.Empty, picker.Size));
                        bitmap.Save(screenshot);
                    }
                    picker.AcceptButton.PerformClick();
                    Assert(picker.SelectedPath == selected, "Path was altered by the selection dialog");
                }
                catch (Exception error)
                {
                    failure = error;
                    picker.Close();
                }
            };
            timer.Start();
            window.Controls.OfType<Button>().Single(b => b.Text == buttonText).PerformClick();
        }
        if (failure != null)
            throw failure;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Run(args);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 1;
        }
    }

    private static void Run(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string folder = Path.GetFullPath(args[0]);
        string run = AppDomain.CurrentDomain.BaseDirectory;
        string actualSlice = Path.Combine(run, "synthetic-a1.gcode.3mf");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(actualSlice, MakeArchive("Bambu Lab A1", "N2S", true));
        using (var stream = File.OpenRead(actualSlice))
            Assert(SliceFile.ReadModel(stream) == "Bambu Lab A1", "A1 fixture not recognized");

        string probe = Path.GetFullPath(args[1]);
        string missing = Path.Combine(folder, "moved.exe");
        string record = Path.Combine(Path.GetDirectoryName(probe), "received.txt");
        Environment.SetEnvironmentVariable("BRIDGE_TEST_PROGRAMS", Path.GetDirectoryName(probe));
        Assert(ConnectLauncher.Locate(missing) == null, "A stale manual path fell back to a different program");
        Assert(ConnectLauncher.Locate(Path.GetFileName(probe)) == null, "An untrusted relative path was accepted");
        var portable = ConnectLauncher.Locate(probe);
        Assert(portable != null && portable.Executable == probe && portable.Source == "手动指定", "Manual location failed");
        var fallback = ConnectLauncher.LocateInstalled(
            new[] { "\"" + missing + "\" \"%1\"", "invalid" }, new[] { missing, probe });
        Assert(fallback != null && fallback.Executable == probe && fallback.Source == "安装位置",
            "Stale registration did not fall back to the installation path");
        var registered = ConnectLauncher.LocateInstalled(
            new[] { "  \"%BRIDGE_TEST_PROGRAMS%\\" + Path.GetFileName(probe) + "\" --profile \"Profile With Spaces\" \"%L\"" },
            new string[0]);
        Assert(registered != null && registered.Executable == probe && registered.Source == "Windows 注册",
            "Registration with environment variables, spaces or %L failed");
        var unquoted = ConnectLauncher.LocateInstalled(new[] { probe + " %1" }, new string[0]);
        Assert(unquoted != null && unquoted.Executable == probe, "Unquoted absolute path with spaces failed");
        string probeSlice = Path.Combine(folder, "测试 (A1)（一） [3] {盘} ' & # + 100%.gcode.3mf");
        registered.OpenSlice(probeSlice);
        Wait(() => File.Exists(record), "The configured Windows program was not launched");
        string[] launchedArgs = File.ReadAllLines(record);
        Assert(launchedArgs.Length == 3 && launchedArgs[0] == "--profile" && launchedArgs[1] == "Profile With Spaces",
            "Installer arguments were dropped");
        string sentUrl = launchedArgs[2];
        var sent = System.Web.HttpUtility.ParseQueryString(Uri.UnescapeDataString(sentUrl.Substring(sentUrl.IndexOf('?'))));
        Assert(sent["path"] == probeSlice, "The program received the wrong slice path");

        var supported = new Dictionary<string, string> {
            {"BL-P001","X1 Carbon"}, {"BL-P002","X1"}, {"C11","P1P"}, {"C12","P1S"}, {"C13","X1E"},
            {"N1","A1 mini"}, {"N2S","A1"}, {"N6","X2D"}, {"N7","P2S"}, {"N8","N8"}, {"N9","A2L"},
            {"N10","H3C"}, {"O1C","H2C"}, {"O1C2","H2C"}, {"O1D","H2D"}, {"O1E","H2D Pro"}, {"O1S","H2S"}
        };
        foreach (var pair in supported)
        {
            using (var data = new MemoryStream(MakeArchive(null, pair.Key, true)))
                Assert(SliceFile.ReadModel(data) == "Bambu Lab " + pair.Value, "ID not recognized: " + pair.Key);
            using (var data = new MemoryStream(MakeArchive("Bambu Lab " + pair.Value, pair.Key, true)))
                Assert(SliceFile.ReadModel(data) == "Bambu Lab " + pair.Value, "Name not recognized: " + pair.Value);
        }
        using (var data = new MemoryStream(MakeArchive("Bambu Lab New Model", "TEST-FUTURE", true)))
            Assert(SliceFile.ReadModel(data) == "Bambu Lab New Model", "New Bambu model name not recognized");

        var sample = MakeArchive("Bambu Lab H3C", "N10", true);
        var old = Path.Combine(folder, "existing.gcode.3mf");
        File.WriteAllBytes(old, sample);
        using (var window = new BridgeWindow())
        {
            window.Show();
            Pump(500);
            var state = Field<Label>(window, "state");
            var tray = Field<NotifyIcon>(window, "tray");
            var menu = Field<ContextMenuStrip>(window, "menu");
            var connectLabel = Field<Label>(window, "connectLabel");
            var connectPath = Field<TextBox>(window, "connectPath");
            Assert(window.Visible && tray.Visible && state.Text.Contains("正在监视"), "Tray/window/startup restore failed");
            Assert(connectLabel.Text.Contains("手动指定") && connectPath.Text == probe, "Manual path was not restored");
            using (var bitmap = new Bitmap(window.Width, window.Height))
            {
                window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
                bitmap.Save(Path.Combine(run, "main-window.png"));
            }
            window.Close();
            Assert(!window.Visible && !window.IsDisposed && tray.Visible, "Closing failed to hide to tray");
            menu.Items[0].PerformClick();
            Assert(window.Visible, "Tray restore failed");
            window.WindowState = FormWindowState.Minimized;
            Pump(500);
            Assert(!window.Visible && tray.Visible, "Minimize failed to hide to tray");
            menu.Items[0].PerformClick();
            Assert(window.Visible && window.WindowState == FormWindowState.Normal, "Tray restore after minimize failed");
            string deepFolder = Path.Combine(folder, "01 Orca（切片）", "02 工位 (A1)", "03 [校验] {完整}", "04 文件 # + 100% & '");
            Directory.CreateDirectory(deepFolder);
            ChoosePath(window, "选择文件夹…", deepFolder, Path.Combine(run, "pasted-folder-check.png"));
            ChoosePath(window, "指定程序…", probe, Path.Combine(run, "pasted-program-check.png"));
            Assert(Field<TextBox>(window, "folderPath").Text == deepFolder, "Pasted folder was not saved");
            Assert(connectPath.Text == probe, "Pasted executable was not saved");
            File.Delete(record);
            string genuineJob = Path.Combine(deepFolder, "实际切片 (A1)（一） [3] {盘} ' & # + 100%.gcode.3mf");
            File.Copy(actualSlice, genuineJob);
            Wait(() => File.Exists(record), "A new Orca export did not reach the manually selected program");
            string genuineUrl = File.ReadAllLines(record).Single();
            var genuineArgs = System.Web.HttpUtility.ParseQueryString(Uri.UnescapeDataString(genuineUrl.Substring(genuineUrl.IndexOf('?'))));
            Assert(genuineArgs["path"] == genuineJob, "The manual program received an incomplete path");
            Assert(genuineArgs["name"] == Path.GetFileName(genuineJob).Substring(0, Path.GetFileName(genuineJob).Length - SliceFile.Extension.Length),
                "The manual program received an incomplete name");
            var automatic = window.Controls.OfType<Button>().Single(b => b.Text == "自动查找");
            automatic.PerformClick();
            var systemConnect = ConnectLauncher.Locate("");
            Assert(systemConnect == null ? connectLabel.Text.Contains("未找到") :
                connectPath.Text == systemConnect.Executable, "Switch back to automatic did not refresh the displayed location");
            var settings = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ConnectPath.txt");
            Assert(File.ReadAllText(settings) == "", "Automatic selection was not saved");

            var watchFolder = Path.Combine(folder, "watch-test");
            Directory.CreateDirectory(watchFolder);
            var oldJob = Path.Combine(watchFolder, "existing.gcode.3mf");
            File.WriteAllBytes(oldJob, sample);
            var calls = new List<string>();
            var reports = new List<string>();
            using (var monitor = new FolderMonitor(watchFolder, window, calls.Add, reports.Add))
            {
                File.WriteAllBytes(Path.Combine(watchFolder, "Bambu filename but wrong machine.gcode.3mf"),
                    MakeArchive("Creality K1C", "N10", true));
                File.WriteAllBytes(Path.Combine(watchFolder, "conflicting machine.gcode.3mf"),
                    MakeArchive("Bambu Lab P1S", "N10", true));
                File.WriteAllBytes(Path.Combine(watchFolder, "not sliced.gcode.3mf"),
                    MakeArchive("Bambu Lab A1", "N2S", false));
                File.WriteAllText(Path.Combine(watchFolder, "damaged.gcode.3mf"), "invalid");
                File.WriteAllBytes(Path.Combine(watchFolder, "ordinary project.3mf"), sample);
                var partial = Path.Combine(watchFolder, "测试 方块 & #1.gcode.3mf");
                using (var f = File.Open(partial, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                {
                    f.Write(sample, 0, sample.Length / 2);
                    f.Flush();
                    Pump(2300);
                    Assert(calls.Count == 0, "Incomplete file handed off");
                    f.Write(sample, sample.Length / 2, sample.Length - sample.Length / 2);
                    f.Flush();
                    Pump(2300);
                    Assert(calls.Count == 0, "Open writer ignored");
                }
                Wait(() => calls.Count == 1, "H3C file was not handed off");
                Assert(calls[0] == partial, "Wrong file was handed off");
                string url = SliceFile.ImportUrl(partial);
                string query = Uri.UnescapeDataString(url.Substring(url.IndexOf('?')));
                var parameters = System.Web.HttpUtility.ParseQueryString(query);
                Assert(parameters["path"] == partial && parameters["name"] == "测试 方块 & #1",
                    "Unicode import path did not roundtrip through the Connect parser");
                var incoming = Path.Combine(watchFolder, "a1-copy.tmp");
                var final = Path.Combine(watchFolder, "a1-copy.gcode.3mf");
                File.Copy(actualSlice, incoming);
                File.Move(incoming, final);
                Wait(() => calls.Count == 2, "Atomic rename was not detected");
                File.WriteAllBytes(oldJob, MakeArchive("Bambu Lab P1P", "C11", true));
                Wait(() => calls.Count == 3, "Overwrite was not detected");
                Pump(1800);
                Assert(calls.SequenceEqual(new[] { partial, final, oldJob }), "Duplicate or wrong handoff");
                Assert(calls.All(File.Exists), "Original was not preserved");
                Assert(reports.Count(r => r.StartsWith("跳过")) == 4, "Invalid files were not all filtered");
            }
            menu.Items[1].PerformClick();
            Assert(window.IsDisposed && !tray.Visible, "Tray exit did not dispose the window");
        }
        Console.WriteLine("PASS: both path dialogs, quoted and invalid paths, nested special-character paths, synthetic A1 handoff, Connect discovery, tray and watcher tests.");
    }
}
