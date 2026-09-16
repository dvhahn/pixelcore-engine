using System;

namespace PixelCore.Runtime.UI;

public static class DialogueTiming
{
    public static float PunctuationSeconds { get; set; } = 0.25f;

    public static float CjkSeconds { get; set; } = 0.08f;

    public static float LatinSeconds { get; set; } = 0.04f;

    public static float TailSeconds { get; set; } = 1f;

    public static float HurrySpeed => 5f;

    public static float StopSeconds(string token) => token.Length >= 2 ? 0.6f : 0.25f;

    public static float ReadSeconds(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0f;

        float total = 0f;
        foreach (char ch in text) total += CharSeconds(ch);
        return total;
    }

    public static float CharSeconds(char ch)
        => IsPunctuation(ch) ? PunctuationSeconds
         : IsLatin(ch) ? LatinSeconds
         : CjkSeconds;

    public static float PageSeconds(string text) => ReadSeconds(text) + TailSeconds;

    private static bool IsPunctuation(char c)
        => c is '.' or ',' or '!' or '?' or ':' or ';' or '…' or '。' or '、' or '！' or '？';

    private static bool IsLatin(char c)
        => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
}
