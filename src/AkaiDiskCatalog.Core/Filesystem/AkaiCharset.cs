using System;
using System.Text;

namespace AkaiDiskCatalog.Core.Filesystem;

/// <summary>
/// AKAI S1000/S3000 names are NOT stored as ASCII: each byte is a code from a small
/// custom character set (0-9, space, A-Z, # + - .). S900 names, by contrast, are plain
/// ASCII. See akai2ascii()/akai2ascii900() in akaiutil.c for the authoritative mapping.
/// </summary>
public static class AkaiCharset
{
    public static char Decode1000(byte c)
    {
        if (c <= 9) return (char)('0' + c);
        if (c == 10) return ' ';
        if (c is >= 11 and <= 36) return (char)('A' + (c - 11));
        if (c == 37) return '#';
        if (c == 38) return '+';
        if (c == 39) return '-';
        if (c == 40) return '.';
        return '.'; // unrecognized code, matches akaiutil's fallback
    }

    public static char Decode900(byte c)
    {
        char a = (char)c;
        if ((a >= '0' && a <= '9') || a == ' ' || (a >= 'A' && a <= 'Z') || (a >= 'a' && a <= 'z')
            || a == '#' || a == '+' || a == '-' || a == '.')
            return a;
        if (c == 0) return ' ';
        return '.';
    }

    /// <summary>
    /// Decodes a fixed-length AKAI name field (not zero-terminated - 0x00 is the digit
    /// '0' in the S1000/S3000 charset) and trims trailing padding spaces.
    /// </summary>
    public static string DecodeName(byte[] data, int offset, int length, bool s900)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
        {
            byte b = data[offset + i];
            sb.Append(s900 ? Decode900(b) : Decode1000(b));
        }
        return sb.ToString().TrimEnd(' ');
    }

    public const int MaxNameLength = 12;

    /// <summary>
    /// Inverse of <see cref="Decode1000"/>. Uppercases the input, validates every character
    /// maps to a valid S1000/S3000 code, and left-justifies/space-pads (code 10) to exactly
    /// <paramref name="length"/> bytes. Rejects (never truncates or silently drops characters)
    /// empty or over-length input, so a rename either fully succeeds or is cleanly refused.
    /// </summary>
    public static byte[] Encode1000(string name, int length = MaxNameLength)
    {
        string trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            throw new AkaiNameEncodeException("Name cannot be empty.");
        if (trimmed.Length > length)
            throw new AkaiNameEncodeException($"Name is too long ({trimmed.Length} characters, max {length}).");

        var buf = new byte[length];
        for (int i = 0; i < length; i++)
        {
            if (i >= trimmed.Length)
            {
                buf[i] = 10; // space
                continue;
            }

            char c = char.ToUpperInvariant(trimmed[i]);
            buf[i] = c switch
            {
                >= '0' and <= '9' => (byte)(c - '0'),
                ' ' => 10,
                >= 'A' and <= 'Z' => (byte)(11 + (c - 'A')),
                '#' => 37,
                '+' => 38,
                '-' => 39,
                '.' => 40,
                _ => throw new AkaiNameEncodeException(
                    $"Character '{trimmed[i]}' is not valid in an AKAI name (allowed: 0-9, space, A-Z, # + - .)."),
            };
        }
        return buf;
    }
}

public sealed class AkaiNameEncodeException : Exception
{
    public AkaiNameEncodeException(string message) : base(message) { }
}
