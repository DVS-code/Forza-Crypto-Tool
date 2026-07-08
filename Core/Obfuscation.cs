using System.Security.Cryptography;
using System.Text;

namespace ForzaCryptoTool;

internal static class Obfuscation
{
    private const string Passphrase = "ForzaCryptoTool/endpoint/obfuscation/v2";

    private static byte[] Keystream(int length)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(Passphrase));
        var stream = new byte[length];
        for (int i = 0; i < length; i++)
            stream[i] = key[i % key.Length];
        return stream;
    }

    public static string Reveal(string obfuscatedBase64)
    {
        var data = Convert.FromBase64String(obfuscatedBase64);
        var key = Keystream(data.Length);
        var clear = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            clear[i] = (byte)(data[i] ^ key[i]);
        return Encoding.UTF8.GetString(clear);
    }

    public static string Hide(string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        var key = Keystream(data.Length);
        var hidden = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            hidden[i] = (byte)(data[i] ^ key[i]);
        return Convert.ToBase64String(hidden);
    }
}
