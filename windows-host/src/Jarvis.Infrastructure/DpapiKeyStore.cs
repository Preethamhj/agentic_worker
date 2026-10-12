using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace Jarvis.Infrastructure;

/// <summary>
/// DPAPI-backed key store protecting the HMAC secret with CurrentUser scope.
/// The key is generated on first run and persisted encrypted.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyStore : IDpapiKeyStore
{
    private readonly string _keyFilePath;
    private readonly object _lock = new();

    public DpapiKeyStore(string? keyFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(keyFilePath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var jarvisDir = Path.Combine(localAppData, "Jarvis");
            _keyFilePath = Path.Combine(jarvisDir, "auth.key.dpapi");
        }
        else
        {
            _keyFilePath = keyFilePath;
        }
    }

    public byte[] GetOrCreateKey()
    {
        lock (_lock)
        {
            if (File.Exists(_keyFilePath))
            {
                var encrypted = File.ReadAllBytes(_keyFilePath);
                return ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            }

            var key = GenerateNewKey();
            SaveKey(key);
            return key;
        }
    }

    public void RegenerateKey()
    {
        lock (_lock)
        {
            var key = GenerateNewKey();
            SaveKey(key);
        }
    }

    private static byte[] GenerateNewKey()
    {
        return RandomNumberGenerator.GetBytes(32);
    }

    private void SaveKey(byte[] key)
    {
        var dir = Path.GetDirectoryName(_keyFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var encrypted = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_keyFilePath, encrypted);
    }
}

/// <summary>
/// In-memory implementation of IDpapiKeyStore for deterministic unit testing.
/// </summary>
public sealed class InMemoryDpapiKeyStore : IDpapiKeyStore
{
    private byte[] _key;
    private readonly object _lock = new();

    public InMemoryDpapiKeyStore(byte[]? initialKey = null)
    {
        _key = initialKey ?? RandomNumberGenerator.GetBytes(32);
    }

    public byte[] GetOrCreateKey()
    {
        lock (_lock)
        {
            return (byte[])_key.Clone();
        }
    }

    public void RegenerateKey()
    {
        lock (_lock)
        {
            _key = RandomNumberGenerator.GetBytes(32);
        }
    }
}
