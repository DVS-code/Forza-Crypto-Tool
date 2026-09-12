namespace ForzaCryptoTool;

internal enum DetectedKind
{
    GameDbEncrypted,
    GameDbDecrypted,
    ProfileData,
    ProfileDecrypted,
    Method22Zip,
    Method22ZipDecrypted,
    PlainZip,
    ConfigFileEncrypted,
    ConfigFileDecrypted,
    Unknown,
}

internal sealed record DetectionResult(
    string Path,
    string FileName,
    DetectedKind Kind,
    long Size,
    DateTime Modified,
    bool Encrypted,
    bool IntegrityOk)
{
    public string KindLabel => Kind switch
    {
        DetectedKind.GameDbEncrypted => "GameDB",
        DetectedKind.GameDbDecrypted => "GameDB (decrypted SQLite)",
        DetectedKind.ProfileData => "Profile Data",
        DetectedKind.ProfileDecrypted => "Profile Data (decrypted)",
        DetectedKind.Method22Zip => "Method 22 ZIP",
        DetectedKind.Method22ZipDecrypted => "Method 22 ZIP (decrypted)",
        DetectedKind.PlainZip => "Plain ZIP (no encryption)",
        DetectedKind.ConfigFileEncrypted => "Config File",
        DetectedKind.ConfigFileDecrypted => "Config File (decrypted)",
        _ => "Unknown",
    };

    public string SizeLabel => Size switch
    {
        >= 1024 * 1024 => $"{Size / (1024.0 * 1024.0):0.0} MB",
        >= 1024 => $"{Size / 1024.0:0.0} KB",
        _ => $"{Size} B",
    };
}

internal static class FileDetection
{
    private static readonly byte[] SqliteMagic = "SQLite format 3\0"u8.ToArray();

    public static DetectionResult Detect(string path)
    {
        var info = new FileInfo(path);
        var header = new byte[4096];
        int read = 0;
        bool readable = true;
        try
        {
            using var stream = File.OpenRead(path);
            read = stream.Read(header, 0, header.Length);
        }
        catch
        {
            readable = false;
        }

        bool isSqlite = read >= SqliteMagic.Length && header.AsSpan(0, SqliteMagic.Length).SequenceEqual(SqliteMagic);
        string name = info.Name.ToLowerInvariant();
        string ext = info.Extension.ToLowerInvariant();

        bool profileName = name.Contains("profiledata") || name.Contains("profilebackup") || name.StartsWith("c_profile");
        bool profileFraming = info.Length > 0x24 && (info.Length - 0x24) % 0x210 == 0;

        bool profileDecrypted = read >= 4 && header[0] == 0xB6 && header[1] == 0xF2 && header[2] == 0x8B && header[3] == 0x4A;

        long cfgBody = info.Length - 0x24;
        uint cfgPad = read >= 20 ? BitConverter.ToUInt32(header, 16) : uint.MaxValue;
        bool configExt = ext is ".ini" or ".cfg" or ".config";
        bool configFraming = info.Length > 0x24 && cfgBody > 0 && cfgBody % 0x210 == 0
            && cfgPad < cfgBody && read > 0x24
            && Entropy(header.AsSpan(0x24, Math.Min(read, header.Length) - 0x24)) > 7.4;

        bool configEncrypted = configFraming && configExt && !profileName;

        bool configDecrypted = (name.Contains("_decrypted") || name.Contains("_edited"))
            && (ext is ".ini" or ".cfg" or ".config" or ".txt" || name.Contains("settings") || name.Contains("physics"));

        bool isZip = read >= 4 && header[0] == 'P' && header[1] == 'K' && header[2] == 3 && header[3] == 4;
        var (hasM22, validZip) = isZip ? ScanZip(path) : (false, false);

        bool method22Zip = hasM22;

        bool method22ZipDecrypted = isZip && !method22Zip
            && (name.Contains("_decrypted") || name.Contains("decrypted") || name.Contains("_edited"));

        bool plainZip = isZip && validZip && !method22Zip && !method22ZipDecrypted;

        DetectedKind kind;
        bool encrypted;
        if (method22Zip)
        {
            kind = DetectedKind.Method22Zip;
            encrypted = true;
        }
        else if (method22ZipDecrypted)
        {
            kind = DetectedKind.Method22ZipDecrypted;
            encrypted = false;
        }
        else if (plainZip)
        {
            kind = DetectedKind.PlainZip;
            encrypted = false;
        }
        else if (profileDecrypted)
        {
            kind = DetectedKind.ProfileDecrypted;
            encrypted = false;
        }
        else if (configDecrypted)
        {
            kind = DetectedKind.ConfigFileDecrypted;
            encrypted = false;
        }
        else if (profileName && !isSqlite)
        {
            kind = DetectedKind.ProfileData;
            encrypted = true;
        }
        else if (isSqlite)
        {
            kind = DetectedKind.GameDbDecrypted;
            encrypted = false;
        }
        else if (ext is ".slt" || name.Contains("gamedb"))
        {
            kind = DetectedKind.GameDbEncrypted;
            encrypted = true;
        }
        else if (configEncrypted)
        {
            kind = DetectedKind.ConfigFileEncrypted;
            encrypted = true;
        }
        else if (profileFraming && !configExt && read > 0 && Entropy(header.AsSpan(0, read)) > 7.4)
        {
            kind = DetectedKind.ProfileData;
            encrypted = true;
        }
        else
        {
            kind = DetectedKind.Unknown;
            encrypted = read > 0 && Entropy(header.AsSpan(0, read)) > 7.4;
        }

        bool integrity = readable && info.Length > 0 && kind != DetectedKind.Unknown;
        return new DetectionResult(path, info.Name, kind, info.Length, info.LastWriteTime, encrypted, integrity);
    }

    private static (bool hasM22, bool validZip) ScanZip(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            bool walkedOne = false;

            for (int entries = 0; entries < 1_000_000; entries++)
            {
                if (stream.Position + 30 > stream.Length) break;
                uint sig = reader.ReadUInt32();
                if (sig == 0x02014B50 || sig == 0x06054B50) break;
                if (sig != 0x04034B50) break;
                reader.ReadUInt16();
                reader.ReadUInt16();
                ushort method = reader.ReadUInt16();
                if (method == 22) return (true, true);
                reader.ReadUInt32();
                reader.ReadUInt32();
                uint csize = reader.ReadUInt32();
                reader.ReadUInt32();
                ushort fnLen = reader.ReadUInt16();
                ushort efLen = reader.ReadUInt16();
                long next = stream.Position + fnLen + efLen + csize;
                if (next <= stream.Position || next > stream.Length) break;
                stream.Position = next;
                walkedOne = true;
            }
            return (false, walkedOne);
        }
        catch
        {
            return (false, false);
        }
    }

    private static double Entropy(ReadOnlySpan<byte> data)
    {
        Span<int> counts = stackalloc int[256];
        foreach (var b in data)
            counts[b]++;
        double entropy = 0;
        foreach (var count in counts)
        {
            if (count == 0) continue;
            double p = (double)count / data.Length;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }
}
