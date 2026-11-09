using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

internal sealed class FolderMonitor : IDisposable
{
    private struct FileStamp : IEquatable<FileStamp>
    {
        internal long Length;
        internal long WriteTime;
        internal long CreationTime;

        public bool Equals(FileStamp other)
        {
            return Length == other.Length && WriteTime == other.WriteTime && CreationTime == other.CreationTime;
        }
    }

    private sealed class PendingFile
    {
        internal FileStamp Stamp;
        internal long ReadyAt;
    }

    private readonly Dictionary<string, FileStamp> seen =
        new Dictionary<string, FileStamp>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PendingFile> pending =
        new Dictionary<string, PendingFile>(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Timer timer = new Timer { Interval = 400 };
    private readonly Action<string> openFile;
    private readonly Action<string> report;
    private readonly FileSystemWatcher watcher;
    internal string Folder { get; private set; }
    internal bool Active { get { return watcher.EnableRaisingEvents; } }

    internal FolderMonitor(string folder, ISynchronizeInvoke window, Action<string> openFile, Action<string> report)
    {
        Folder = Path.GetFullPath(folder);
        this.openFile = openFile;
        this.report = report;
        foreach (string path in ListFiles())
        {
            FileStamp stamp;
            if (TryStamp(path, out stamp))
                seen[path] = stamp;
        }

        watcher = new FileSystemWatcher(Folder, "*" + SliceFile.Extension) {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            SynchronizingObject = window
        };
        timer.Tick += Scan;
        watcher.Created += OnChange;
        watcher.Changed += OnChange;
        watcher.Deleted += OnChange;
        watcher.Renamed += OnChange;
        watcher.Error += OnError;
        try
        {
            watcher.EnableRaisingEvents = true;
            timer.Start();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private IEnumerable<string> ListFiles()
    {
        foreach (var path in Directory.EnumerateFiles(Folder, "*" + SliceFile.Extension))
            if (path.EndsWith(SliceFile.Extension, StringComparison.OrdinalIgnoreCase))
                yield return path;
    }

    private static bool TryStamp(string path, out FileStamp stamp)
    {
        var file = new FileInfo(path);
        stamp = default(FileStamp);
        if (!file.Exists)
            return false;
        stamp = new FileStamp { Length = file.Length, WriteTime = file.LastWriteTimeUtc.Ticks,
            CreationTime = file.CreationTimeUtc.Ticks };
        return true;
    }

    private bool disposed;

    private void OnChange(object sender, FileSystemEventArgs args)
    {
        if (!disposed && Active)
            timer.Start();
    }

    private void OnError(object sender, ErrorEventArgs args)
    {
        if (disposed)
            return;
        if (args.GetException() is InternalBufferOverflowException)
            timer.Start();
        else
            StopMonitoring(args.GetException().Message);
    }

    private void StopMonitoring(string reason)
    {
        timer.Stop();
        watcher.EnableRaisingEvents = false;
        report("监视已停止，请重新选择文件夹。 " + reason);
    }

    private void Scan(object sender, EventArgs args)
    {
        try
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in ListFiles())
            {
                FileStamp stamp, previous;
                if (!TryStamp(path, out stamp))
                    continue;
                present.Add(path);
                if (seen.TryGetValue(path, out previous) && previous.Equals(stamp))
                {
                    pending.Remove(path);
                    continue;
                }
                PendingFile job;
                if (!pending.TryGetValue(path, out job) || !job.Stamp.Equals(stamp))
                {
                    pending[path] = new PendingFile { Stamp = stamp, ReadyAt = clock.ElapsedMilliseconds + 1400 };
                    continue;
                }
                if (clock.ElapsedMilliseconds >= job.ReadyAt && TryHandoff(path, job))
                {
                    seen[path] = stamp;
                    pending.Remove(path);
                }
            }
            foreach (string path in new List<string>(seen.Keys))
                if (!present.Contains(path))
                    seen.Remove(path);
            foreach (string path in new List<string>(pending.Keys))
                if (!present.Contains(path))
                    pending.Remove(path);
            if (pending.Count == 0)
                timer.Stop();
        }
        catch (IOException error)
        {
            StopMonitoring(error.Message);
        }
        catch (UnauthorizedAccessException error)
        {
            StopMonitoring(error.Message);
        }
    }

    private bool TryHandoff(string path, PendingFile job)
    {
        string name = Path.GetFileName(path);
        FileStream stream;
        try
        {
            // A still-open writer cannot coexist with this sharing mode.
            stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            report("无法读取：" + name);
            return true;
        }

        using (stream)
        {
            FileStamp current;
            if (!TryStamp(path, out current) || !current.Equals(job.Stamp))
                return false;
            string model;
            try
            {
                model = SliceFile.ReadModel(stream);
            }
            catch (Exception error)
            {
                if (!(error is InvalidDataException || error is IOException || error is System.Xml.XmlException ||
                    error is ArgumentException || error is InvalidOperationException))
                    throw;
                report("跳过无法读取的切片：" + name);
                return true;
            }
            if (model == null)
            {
                report("跳过未识别为拓竹的切片：" + name);
                return true;
            }
            try
            {
                openFile(path);
                report("已交给 Bambu Connect · " + model + Environment.NewLine + name);
                return true;
            }
            catch (Win32Exception error)
            {
                report("暂时无法打开 Bambu Connect，10 秒后重试。" + Environment.NewLine + error.Message);
                job.ReadyAt = clock.ElapsedMilliseconds + 10000;
                return false;
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        timer.Dispose();
        watcher.Dispose();
    }
}
