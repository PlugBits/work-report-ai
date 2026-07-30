using System.Security.Cryptography;

namespace WorkLogAI.Infrastructure;

/// <summary>
/// Pure file round-trip helpers for the MSAL token cache blob. Encryption is
/// injected so the round-trip and corrupt-file behaviour can be unit tested
/// without touching DPAPI (which is Windows-only).
/// </summary>
public static class TokenCacheSerializer
{
    public static byte[]? ReadCache(string path, Func<byte[], byte[]?> unprotect)
    {
        ArgumentNullException.ThrowIfNull(unprotect);
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var encrypted = File.ReadAllBytes(path);
            if (encrypted.Length == 0)
            {
                return null;
            }

            return unprotect(encrypted);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public static void WriteCache(string path, byte[] plainText, Func<byte[], byte[]> protect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(plainText);
        ArgumentNullException.ThrowIfNull(protect);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var encrypted = protect(plainText);
        File.WriteAllBytes(path, encrypted);
    }

    public static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
