using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;

internal sealed class ConnectLauncher
{
    private const string ProtocolCommand = @"Software\Classes\bambu-connect\shell\open\command";
    private const string AppPath = @"Software\Microsoft\Windows\CurrentVersion\App Paths\Bambu Connect.exe";
    private static readonly Regex CommandParts = new Regex(
        "^\\s*(?:\"([^\"]+\\.exe)\"|([^\"]+?\\.exe))(?=\\s|$)(.*)$", RegexOptions.IgnoreCase);
    private static readonly Regex UrlPlaceholder = new Regex("%[1l*](?![A-Za-z0-9_])", RegexOptions.IgnoreCase);

    internal readonly string Executable;
    internal readonly string Source;
    private readonly string argumentTemplate;

    private ConnectLauncher(string executable, string source, string argumentTemplate)
    {
        Executable = executable;
        Source = source;
        this.argumentTemplate = argumentTemplate;
    }

    internal static ConnectLauncher Locate(string manuallySelected)
    {
        if (!String.IsNullOrWhiteSpace(manuallySelected))
            return FromPath(manuallySelected, "手动指定");
        return LocateInstalled(RegistryValues(ProtocolCommand), InstalledPaths());
    }

    internal static ConnectLauncher LocateInstalled(IEnumerable<string> protocolCommands, IEnumerable<string> paths)
    {
        foreach (string command in protocolCommands)
        {
            var parts = CommandParts.Match(command ?? "");
            if (!parts.Success)
                continue;
            string executable = ExistingPath(parts.Groups[1].Success ? parts.Groups[1].Value : parts.Groups[2].Value);
            if (executable != null)
                return new ConnectLauncher(executable, "Windows 注册", parts.Groups[3].Value.Trim());
        }
        foreach (string path in paths)
        {
            var candidate = FromPath(path, "安装位置");
            if (candidate != null)
                return candidate;
        }
        return null;
    }

    private static ConnectLauncher FromPath(string path, string source)
    {
        string executable = ExistingPath(path);
        return executable == null ? null : new ConnectLauncher(executable, source, "");
    }

    private static string ExistingPath(string value)
    {
        if (String.IsNullOrWhiteSpace(value))
            return null;
        string path = value.Trim().Trim('"');
        if (IsExistingExecutable(path))
            return path;
        string expanded = Environment.ExpandEnvironmentVariables(path);
        return IsExistingExecutable(expanded) ? expanded : null;
    }

    private static bool IsExistingExecutable(string path)
    {
        // Use an explicit path. Do not search the inbox, current directory, or PATH.
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            (path.StartsWith(@"\\", StringComparison.Ordinal) ||
                (path.Length > 3 && path[1] == ':' && (path[2] == '\\' || path[2] == '/'))) &&
            File.Exists(path);
    }

    private static IEnumerable<string> RegistryValues(string key)
    {
        var hives = new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
        var views = Environment.Is64BitOperatingSystem
            ? new[] { RegistryView.Registry64, RegistryView.Registry32 } : new[] { RegistryView.Registry32 };
        foreach (var hive in hives)
            foreach (var view in views)
            {
                string value = ReadRegistry(hive, view, key);
                if (!String.IsNullOrWhiteSpace(value))
                    yield return value;
            }
    }

    private static string ReadRegistry(RegistryHive hive, RegistryView view, string key)
    {
        try
        {
            using (var root = RegistryKey.OpenBaseKey(hive, view))
            using (var entry = root.OpenSubKey(key))
                return entry == null ? null : entry.GetValue("") as string;
        }
        catch (SecurityException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }

    private static IEnumerable<string> InstalledPaths()
    {
        foreach (string path in RegistryValues(AppPath))
            yield return path;

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!String.IsNullOrWhiteSpace(local))
            yield return Path.Combine(local, "Programs", "bambu-connect", "Bambu Connect.exe");

        var roots = new[] {
            Environment.GetEnvironmentVariable("ProgramW6432"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        foreach (string root in roots)
        {
            if (String.IsNullOrWhiteSpace(root))
                continue;
            yield return Path.Combine(root, "Bambu Connect", "Bambu Connect.exe");
            yield return Path.Combine(root, "bambu-connect", "Bambu Connect.exe");
        }
    }

    internal void OpenSlice(string path)
    {
        string url = SliceFile.ImportUrl(path);
        string options = Environment.ExpandEnvironmentVariables(argumentTemplate);
        // Keep the installer's arguments and pass the URL directly so the shell cannot change its escaping.
        string arguments = UrlPlaceholder.IsMatch(options)
            ? UrlPlaceholder.Replace(options, delegate { return url; })
            : (options + " \"" + url + "\"").TrimStart();
        var start = new ProcessStartInfo(Executable, arguments) {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(Executable)
        };
        using (var process = Process.Start(start)) { }
    }
}
