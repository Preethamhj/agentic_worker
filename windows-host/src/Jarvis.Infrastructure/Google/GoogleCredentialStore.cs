
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Jarvis.Infrastructure.Google;

[SupportedOSPlatform("windows")]
public sealed class GoogleCredentialStore :
    IGoogleCredentialStore, IDisposable
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("Jarvis.Google.Credentials.v1");

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _directory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public GoogleCredentialStore(GoogleOAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        _directory = options.CredentialDirectoryPath;
        Directory.CreateDirectory(_directory);
    }

    // Google IDataStore methods
    // Exact signatures required by Google.Apis.Auth 1.78.0

    public async Task StoreAsync<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var json = JsonSerializer.SerializeToUtf8Bytes(
            value, JsonOptions);

        await WriteProtectedAsync(
            GetTokenPath<T>(key),
            json);
    }

    public async Task<T> GetAsync<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var json = await ReadProtectedAsync(
            GetTokenPath<T>(key));

        if (json is null)
            return default!;

        return JsonSerializer.Deserialize<T>(
            json, JsonOptions)!;
    }

    public async Task DeleteAsync<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _lock.WaitAsync();

        try
        {
            var path = GetTokenPath<T>(key);

            if (File.Exists(path))
                File.Delete(path);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync()
    {
        await _lock.WaitAsync();

        try
        {
            foreach (var path in Directory.EnumerateFiles(
                _directory, "token-*.bin"))
            {
                File.Delete(path);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    // Account information storage

    public async Task StoreAccountAsync(
        GoogleAccountInfo account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            account.AccountId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            account.Email);

        var json = JsonSerializer.SerializeToUtf8Bytes(
            account, JsonOptions);

        await WriteProtectedAsync(
            GetAccountPath(),
            json,
            cancellationToken);
    }

    public async Task<GoogleAccountInfo?> GetAccountAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await ReadProtectedAsync(
            GetAccountPath(),
            cancellationToken);

        if (json is null)
            return null;

        return JsonSerializer.Deserialize<GoogleAccountInfo>(
            json, JsonOptions);
    }

    public async Task ClearAccountAsync(
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            var path = GetAccountPath();

            if (File.Exists(path))
                File.Delete(path);
        }
        finally
        {
            _lock.Release();
        }
    }

    // DPAPI protection

    private async Task WriteProtectedAsync(
        string path,
        byte[] plainText,
        CancellationToken cancellationToken = default)
    {
        var encrypted = ProtectedData.Protect(
            plainText,
            Entropy,
            DataProtectionScope.CurrentUser);

        await _lock.WaitAsync(cancellationToken);

        var temporaryPath = path + "." +
            Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                encrypted,
                cancellationToken);

            File.Move(
                temporaryPath,
                path,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);

            _lock.Release();
        }
    }

    private async Task<byte[]?> ReadProtectedAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(path))
                return null;

            var encrypted = await File.ReadAllBytesAsync(
                path, cancellationToken);

            return ProtectedData.Unprotect(
                encrypted,
                Entropy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            _lock.Release();
        }
    }

    private string GetTokenPath<T>(string key)
    {
        var identity = key + "|" + typeof(T).FullName;

        var hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(identity));

        return Path.Combine(
            _directory,
            "token-" + Convert.ToHexString(hash) + ".bin");
    }

    private string GetAccountPath()
    {
        return Path.Combine(_directory, "account.bin");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _lock.Dispose();
        _disposed = true;
    }
}
