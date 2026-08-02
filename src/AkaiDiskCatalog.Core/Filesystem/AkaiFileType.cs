namespace AkaiDiskCatalog.Core.Filesystem;

public enum AkaiFileKind
{
    Unknown,
    Sample,
    Program,
    Drum,
    Effects,
    QuickLook,
    TakeList,
    Multi,
    System,
    CdSetup,
    OverallSettings900,
    Fixup900,
    MemoryImage900,
}

public enum AkaiPlatform
{
    Unknown,
    S900,
    S1000,
    S3000
}

/// <summary>
/// Maps a raw AKAI volume-directory file-type byte to a (platform, kind) pair.
/// See akaiutil_file.h for the authoritative byte code list.
/// </summary>
public static class AkaiFileType
{
    public static (AkaiPlatform Platform, AkaiFileKind Kind) Classify(byte typeByte)
    {
        // S3000 codes are the S1000 lowercase-letter code with the 0x80 bit set.
        if (typeByte >= 0x80)
        {
            byte baseCode = (byte)(typeByte - 0x80);
            return baseCode switch
            {
                (byte)'s' => (AkaiPlatform.S3000, AkaiFileKind.Sample),
                (byte)'p' => (AkaiPlatform.S3000, AkaiFileKind.Program),
                (byte)'h' => (AkaiPlatform.S3000, AkaiFileKind.Sample), // CD sample params
                (byte)'m' => (AkaiPlatform.S3000, AkaiFileKind.Multi),
                (byte)'c' => (AkaiPlatform.S3000, AkaiFileKind.System),
                _ => (AkaiPlatform.S3000, AkaiFileKind.Unknown),
            };
        }

        return typeByte switch
        {
            (byte)'s' => (AkaiPlatform.S1000, AkaiFileKind.Sample),
            (byte)'p' => (AkaiPlatform.S1000, AkaiFileKind.Program),
            (byte)'d' => (AkaiPlatform.S1000, AkaiFileKind.Drum),
            (byte)'x' => (AkaiPlatform.S1000, AkaiFileKind.Effects),
            (byte)'q' => (AkaiPlatform.S1000, AkaiFileKind.QuickLook),
            (byte)'t' => (AkaiPlatform.S1000, AkaiFileKind.TakeList),
            (byte)'c' => (AkaiPlatform.S1000, AkaiFileKind.System),
            (byte)'T' => (AkaiPlatform.S3000, AkaiFileKind.CdSetup),
            (byte)'S' => (AkaiPlatform.S900, AkaiFileKind.Sample),
            (byte)'P' => (AkaiPlatform.S900, AkaiFileKind.Program),
            (byte)'D' => (AkaiPlatform.S900, AkaiFileKind.Drum),
            (byte)'O' => (AkaiPlatform.S900, AkaiFileKind.OverallSettings900),
            (byte)'F' => (AkaiPlatform.S900, AkaiFileKind.Fixup900),
            (byte)'M' => (AkaiPlatform.S900, AkaiFileKind.MemoryImage900),
            0x00 => (AkaiPlatform.Unknown, AkaiFileKind.Unknown), // free entry
            _ => (AkaiPlatform.Unknown, AkaiFileKind.Unknown),
        };
    }

    public static string Describe(byte typeByte)
    {
        var (platform, kind) = Classify(typeByte);
        if (platform == AkaiPlatform.Unknown && kind == AkaiFileKind.Unknown)
            return typeByte == 0 ? "(free)" : $"unknown (0x{typeByte:X2})";
        return $"{platform} {kind}".Replace("S900 ", "S900 ").Trim();
    }
}
