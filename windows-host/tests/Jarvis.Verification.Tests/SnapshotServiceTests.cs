using FluentAssertions;
using Jarvis.Contracts;
using Xunit;

namespace Jarvis.Verification.Tests;

public sealed class SnapshotServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"jarvis-snapshot-{Guid.NewGuid():N}");

    public SnapshotServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Capture_Is_NonRecursive_And_Does_Not_Hash_Unchanged_Entries()
    {
        var unchanged = Path.Combine(_root, "unchanged.txt");
        File.WriteAllText(unchanged, "unchanged");
        var nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "nested.txt"), "not in the direct scope");

        var capture = new SnapshotService().Capture([_root]);

        capture.Entries.Select(entry => entry.Path).Should().Contain(Path.GetFullPath(unchanged));
        capture.Entries.Select(entry => entry.Path).Should().Contain(Path.GetFullPath(nested));
        capture.Entries.Select(entry => entry.Path).Should().NotContain(Path.GetFullPath(Path.Combine(nested, "nested.txt")));
        capture.Entries.Single(entry => entry.Path == Path.GetFullPath(unchanged)).Sha256.Should().BeNull();
    }

    [Fact]
    public void Capture_Hashes_Only_Metadata_Changes_And_The_Authorized_Target()
    {
        var changed = Path.Combine(_root, "changed.txt");
        var unchanged = Path.Combine(_root, "unchanged.txt");
        var target = Path.Combine(_root, "authorized.txt");
        File.WriteAllText(changed, "before");
        File.WriteAllText(unchanged, "unchanged");

        var service = new SnapshotService();
        var before = service.Capture([_root]);
        File.WriteAllText(changed, "content changed and larger");
        File.WriteAllText(target, "authorized creation");

        var after = service.Capture(
            [_root],
            previousEntries: before.Entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase),
            authorizedTarget: target);

        after.Entries.Single(entry => entry.Path == Path.GetFullPath(changed)).Sha256.Should().NotBeNullOrEmpty();
        after.Entries.Single(entry => entry.Path == Path.GetFullPath(target)).Sha256.Should().NotBeNullOrEmpty();
        after.Entries.Single(entry => entry.Path == Path.GetFullPath(unchanged)).Sha256.Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
