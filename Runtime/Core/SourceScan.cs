using System.Text;

namespace PixelCore.Runtime.Core;

public static class SourceScan
{
    public static string StripComments(string line, ref bool inBlock)
    {
        var sb = new StringBuilder(line.Length);
        bool inStr = false, inChar = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            char n = i + 1 < line.Length ? line[i + 1] : '\0';

            if (inBlock)
            {
                if (c == '*' && n == '/') { inBlock = false; i++; }
                continue;
            }
            if (inStr)
            {
                sb.Append(c);
                if (c == '\\' && n != '\0') { sb.Append(n); i++; }
                else if (c == '"') inStr = false;
                continue;
            }
            if (inChar)
            {
                sb.Append(c);
                if (c == '\\' && n != '\0') { sb.Append(n); i++; }
                else if (c == '\'') inChar = false;
                continue;
            }
            if (c == '/' && n == '/') break;
            if (c == '/' && n == '*') { inBlock = true; i++; continue; }
            if (c == '"') { inStr = true; sb.Append(c); continue; }
            if (c == '\'') { inChar = true; sb.Append(c); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static string StripCommentsAndStrings(string line, ref bool inBlock, ref bool inVerbatim)
    {
        var sb = new StringBuilder(line.Length);
        bool inStr = false, inChar = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            char n = i + 1 < line.Length ? line[i + 1] : '\0';

            if (inBlock)
            {
                if (c == '*' && n == '/') { inBlock = false; i++; }
                continue;
            }
            if (inVerbatim)
            {
                if (c != '"') continue;
                if (n == '"') { i++; continue; }
                inVerbatim = false; sb.Append('"');
                continue;
            }
            if (inStr)
            {
                if (c == '\\' && n != '\0') { i++; continue; }
                if (c == '"') { inStr = false; sb.Append('"'); }
                continue;
            }
            if (inChar)
            {
                if (c == '\\' && n != '\0') { i++; continue; }
                if (c == '\'') { inChar = false; sb.Append('\''); }
                continue;
            }
            if (c == '/' && n == '/') break;
            if (c == '/' && n == '*') { inBlock = true; i++; continue; }
            if (c == '@' && n == '"') { inVerbatim = true; i++; sb.Append('"'); continue; }
            if (c == '"') { inStr = true; sb.Append(c); continue; }
            if (c == '\'') { inChar = true; sb.Append(c); continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }
}
