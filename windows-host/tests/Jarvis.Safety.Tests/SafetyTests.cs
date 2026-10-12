using FluentAssertions;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Xunit;

namespace Jarvis.Safety.Tests;

public class SafetyTests
{
    [Fact]
    public void InMemoryDpapiKeyStore_Generates_And_Persists_Key()
    {
        var keyStore = new InMemoryDpapiKeyStore();
        var key1 = keyStore.GetOrCreateKey();
        key1.Should().HaveCount(32);

        var key2 = keyStore.GetOrCreateKey();
        key2.Should().Equal(key1, "GetOrCreateKey should return identical key until regenerated");

        keyStore.RegenerateKey();
        var key3 = keyStore.GetOrCreateKey();
        key3.Should().NotEqual(key1, "Regenerated key should differ from original key");
    }

    [Fact]
    public void DpapiKeyStore_On_Windows_Encrypts_And_Decrypts_Key_Persistently()
    {
        if (!OperatingSystem.IsWindows()) return;

        var tempKeyFile = Path.Combine(Path.GetTempPath(), $"jarvis_test_dpapi_{Guid.NewGuid():N}.key");
        try
        {
            var store1 = new DpapiKeyStore(tempKeyFile);
            var key1 = store1.GetOrCreateKey();
            key1.Should().HaveCount(32);

            File.Exists(tempKeyFile).Should().BeTrue();
            var encryptedBytes = File.ReadAllBytes(tempKeyFile);
            encryptedBytes.Should().NotEqual(key1, "Persisted DPAPI file must not store plaintext key");

            var store2 = new DpapiKeyStore(tempKeyFile);
            var key2 = store2.GetOrCreateKey();
            key2.Should().Equal(key1, "Decrypted key from fresh store must match original key");
        }
        finally
        {
            if (File.Exists(tempKeyFile)) File.Delete(tempKeyFile);
        }
    }
}
