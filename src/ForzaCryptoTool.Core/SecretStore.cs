using System.Security.Cryptography;
using System.Text;

namespace ForzaCryptoTool;

internal static class SecretStore
{

    public static byte[] Protect(string plaintext, string purpose)
    {
        var clear = Encoding.UTF8.GetBytes(plaintext);

        if (OperatingSystem.IsWindows())
            return System.Security.Cryptography.ProtectedData.Protect(clear, PurposeEntropy(purpose), DataProtectionScope.CurrentUser);

        return PortableProtect(clear, purpose);
    }

    public static string? Unprotect(byte[] stored, string purpose)
    {
        try
        {
            var clear = OperatingSystem.IsWindows()
                ? System.Security.Cryptography.ProtectedData.Unprotect(stored, PurposeEntropy(purpose), DataProtectionScope.CurrentUser)
                : PortableUnprotect(stored, purpose);
            return clear is null ? null : Encoding.UTF8.GetString(clear);
        }
        catch
        {
            return null;
        }
    }

    public static void Write(string path, string plaintext, string purpose)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Protect(plaintext, purpose));
        RestrictToOwner(path);
    }

    public static string? Read(string path, string purpose)
    {
        try
        {
            return File.Exists(path) ? Unprotect(File.ReadAllBytes(path), purpose) : null;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] PurposeEntropy(string purpose) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"ForzaCryptoTool/secret/{purpose}"));

    private const byte PortableVersion = 1;

    private static byte[] PortableProtect(byte[] clear, string purpose)
    {
        var key = DeriveKey(purpose);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[clear.Length];
        var tag = new byte[16];

        using (var gcm = new AesGcm(key, 16))
            gcm.Encrypt(nonce, clear, cipher, tag);

        var output = new byte[1 + nonce.Length + tag.Length + cipher.Length];
        output[0] = PortableVersion;
        nonce.CopyTo(output, 1);
        tag.CopyTo(output, 1 + nonce.Length);
        cipher.CopyTo(output, 1 + nonce.Length + tag.Length);
        return output;
    }

    private static byte[]? PortableUnprotect(byte[] stored, string purpose)
    {
        if (stored.Length < 1 + 12 + 16 || stored[0] != PortableVersion)
            return null;

        var key = DeriveKey(purpose);
        var nonce = stored.AsSpan(1, 12).ToArray();
        var tag = stored.AsSpan(13, 16).ToArray();
        var cipher = stored.AsSpan(29).ToArray();
        var clear = new byte[cipher.Length];

        using var gcm = new AesGcm(key, 16);
        gcm.Decrypt(nonce, cipher, tag, clear);
        return clear;
    }

    private static byte[] DeriveKey(string purpose)
    {
        var material = new StringBuilder();
        material.Append("ForzaCryptoTool/v3/");
        material.Append(purpose);
        material.Append('\0');
        material.Append(Environment.UserName);
        material.Append('\0');
        material.Append(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        material.Append('\0');
        material.Append(ReadMachineId() ?? Environment.MachineName);

        var salt = SHA256.HashData(Encoding.UTF8.GetBytes("ForzaCryptoTool/secretstore/salt/v1"));
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(material.ToString()), salt, iterations: 100_000, HashAlgorithmName.SHA256, 32);
    }

    private static string? ReadMachineId()
    {
        foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
        {
            try
            {
                if (File.Exists(path))
                {
                    var id = File.ReadAllText(path).Trim();
                    if (id.Length > 0) return id;
                }
            }
            catch { }
        }
        return null;
    }

    private static void RestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { }
    }
}
