using System;
using System.IO;
using System.Text;

namespace PixelCore.Runtime.Core;

public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
        => Write(path, tmp => File.WriteAllText(tmp, contents));

    public static void WriteAllText(string path, string contents, Encoding encoding)
        => Write(path, tmp => File.WriteAllText(tmp, contents, encoding));

    private static void Write(string path, Action<string> writeTemp)
    {
        var tmp = path + ".tmp";
        try
        {
            writeTemp(tmp);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
