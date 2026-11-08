using FluentAssertions;
using Jarvis.Infrastructure.Google;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class GoogleCredentialStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "Jarvis.GoogleCredentialStore.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StoreAsync_ProtectsTokenAndGetAsyncRestoresIt()
    {
        var store = CreateStore();
        var token = new TokenFixture("access-token-placeholder", 123);

        await store.StoreAsync("primary", token);

        var restored = await store.GetAsync<TokenFixture>("primary");
        var files = Directory.GetFiles(_directory, "token-*.bin");

        restored.Should().BeEquivalentTo(token);
        files.Should().ContainSingle();
        File.ReadAllText(files[0]).Should().NotContain("access-token-placeholder");
    }

    [Fact]
    public async Task AccountCanBeStoredLoadedAndCleared()
    {
        var store = CreateStore();
        var account = new GoogleAccountInfo(
            "stable-subject",
            "owner@example.invalid",
            "Jarvis Test",
            DateTimeOffset.UtcNow);

        await store.StoreAccountAsync(account);

        (await store.GetAccountAsync()).Should().BeEquivalentTo(account);

        await store.ClearAccountAsync();

        (await store.GetAccountAsync()).Should().BeNull();
    }

    [Fact]
    public async Task ClearAsync_RemovesStoredTokens()
    {
        var store = CreateStore();
        await store.StoreAsync("primary", new TokenFixture("token", 1));

        await store.ClearAsync();

        (await store.GetAsync<TokenFixture>("primary")).Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private GoogleCredentialStore CreateStore()
    {
        return new GoogleCredentialStore(new GoogleOAuthOptions
        {
            CredentialDirectoryPath = _directory
        });
    }

    private sealed record TokenFixture(string AccessToken, int ExpiresInSeconds);
}
