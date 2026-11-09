using System;
using System.IO;

internal static class ConnectProbe
{
    private static void Main(string[] args)
    {
        string directory = AppDomain.CurrentDomain.BaseDirectory;
        string pending = Path.Combine(directory, "received.tmp");
        File.WriteAllLines(pending, args);
        File.Move(pending, Path.Combine(directory, "received.txt"));
    }
}
