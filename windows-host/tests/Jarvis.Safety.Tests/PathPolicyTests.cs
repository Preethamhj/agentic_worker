using FluentAssertions;
using Jarvis.Safety;
using Xunit;

namespace Jarvis.Safety.Tests;

public class PathPolicyTests
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "Jarvis_PathPolicy_Tests");
    private readonly PathPolicy _policy;

    public PathPolicyTests()
    {
        Directory.CreateDirectory(TempRoot);
        _policy = new PathPolicy(new[] { TempRoot });
    }

    public static IEnumerable<object[]> GetBlockedPaths()
    {
        // 1-4 Directory traversal
        yield return new object[] { Path.Combine(TempRoot, @"..\..\Windows\System32"), "Directory traversal escaping root" };
        yield return new object[] { @"..\..\secret.txt", "Relative directory traversal" };
        yield return new object[] { Path.Combine(TempRoot, @"sub\..\..\escaped.txt"), "Embedded directory traversal" };
        yield return new object[] { TempRoot + @"/../escaped.txt", "Directory traversal with forward slash" };

        // 5-6 UNC paths
        yield return new object[] { @"\\fileserver\share\doc.txt", "UNC path with hostname" };
        yield return new object[] { @"\\127.0.0.1\c$\passwords.txt", "UNC path with IP" };

        // 7-10 Device paths
        yield return new object[] { @"\\.\PhysicalDrive0", "Device path PhysicalDrive" };
        yield return new object[] { @"\\.\COM1", "Device path COM" };
        yield return new object[] { @"\\?\C:\Windows\System32", "Device path Win32 namespace" };
        yield return new object[] { @"\??\C:\Windows\System32", "Device path NT namespace" };

        // 11-16 Reserved DOS device names
        yield return new object[] { Path.Combine(TempRoot, "CON"), "Reserved DOS device CON" };
        yield return new object[] { Path.Combine(TempRoot, "nul.txt"), "Reserved DOS device NUL" };
        yield return new object[] { Path.Combine(TempRoot, "PRN.dat"), "Reserved DOS device PRN" };
        yield return new object[] { Path.Combine(TempRoot, @"sub\aux"), "Reserved DOS device AUX in subfolder" };
        yield return new object[] { Path.Combine(TempRoot, "com1.log"), "Reserved DOS device COM1" };
        yield return new object[] { Path.Combine(TempRoot, "lpt1.txt"), "Reserved DOS device LPT1" };

        // 17-19 Alternate data streams (ADS)
        yield return new object[] { Path.Combine(TempRoot, "file.txt:hidden"), "Alternate data stream" };
        yield return new object[] { Path.Combine(TempRoot, "file.txt::$DATA"), "Alternate data stream $DATA" };
        yield return new object[] { Path.Combine(TempRoot, "dir:stream:$INDEX_ALLOCATION"), "Alternate data stream index" };

        // 20-23 Trailing dots and spaces
        yield return new object[] { Path.Combine(TempRoot, "file.txt."), "Trailing dot on filename" };
        yield return new object[] { Path.Combine(TempRoot, "folder.."), "Trailing multiple dots" };
        yield return new object[] { Path.Combine(TempRoot, "file.txt "), "Trailing space on filename" };
        yield return new object[] { Path.Combine(TempRoot, @"folder \file.txt"), "Trailing space on directory segment" };

        // 24 Over-long path (> 260 chars)
        var longSegment = new string('a', 270);
        yield return new object[] { Path.Combine(TempRoot, longSegment), "Path exceeding MAX_PATH" };

        // 25 Mixed separators
        yield return new object[] { TempRoot + @"\sub/folder\mixed/file.txt", "Mixed slashes separators" };

        // 26-28 Out of allowed roots & invalid characters
        yield return new object[] { @"C:\Windows\System32\cmd.exe", "Absolute path outside allowed root" };
        yield return new object[] { @"C:\Program Files\Common Files\lib.dll", "Program Files outside allowed root" };
        yield return new object[] { Path.Combine(TempRoot, "file\0evil.exe"), "Null byte injection" };
        yield return new object[] { Path.Combine(TempRoot, "bad<char>.txt"), "Illegal filename characters" };
    }

    [Theory]
    [MemberData(nameof(GetBlockedPaths))]
    public void Blocked_Paths_Are_Rejected(string inputPath, string reason)
    {
        var result = _policy.ValidatePath(inputPath);
        result.IsValid.Should().BeFalse($"Path '{inputPath}' should be blocked because: {reason}");
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    public static IEnumerable<object[]> GetAcceptedPaths()
    {
        // 1 Direct file inside root
        yield return new object[] { Path.Combine(TempRoot, "notes.txt"), "Simple filename inside allowed root" };

        // 2 Deeply nested subfolders
        yield return new object[] { Path.Combine(TempRoot, @"projects\2026\q4\budget.xlsx"), "Nested subfolder structure" };

        // 3 Spaces in folder and file name
        yield return new object[] { Path.Combine(TempRoot, @"My Documents\Project Report Final.pdf"), "Spaces in segments" };

        // 4 Latin accents Unicode
        yield return new object[] { Path.Combine(TempRoot, @"café\résumé.txt"), "Unicode Latin accents" };

        // 5 German Unicode characters
        yield return new object[] { Path.Combine(TempRoot, @"Überweisung\groß.dat"), "Unicode German characters" };

        // 6 Japanese Unicode
        yield return new object[] { Path.Combine(TempRoot, @"ドキュメント\プロジェクト.txt"), "Unicode Japanese characters" };

        // 7 Kannada Unicode file
        yield return new object[] { Path.Combine(TempRoot, "ವರದಿ.txt"), "Unicode Kannada file" };

        // 8 Kannada nested folders
        yield return new object[] { Path.Combine(TempRoot, @"ಕಡತಕೋಶ\ಪುಸ್ತಕ.txt"), "Unicode Kannada nested folders" };

        // 9 Case variation matching allowed root
        yield return new object[] { TempRoot.ToLowerInvariant() + @"\sub\file.txt", "Case variation within allowed root" };

        // 10 Hyphens and underscores
        yield return new object[] { Path.Combine(TempRoot, @"data-repo_v1\test_file-final.json"), "Hyphens and underscores" };
    }

    [Theory]
    [MemberData(nameof(GetAcceptedPaths))]
    public void Accepted_Paths_Are_Allowed(string inputPath, string description)
    {
        var result = _policy.ValidatePath(inputPath);
        result.IsValid.Should().BeTrue($"Path '{inputPath}' should be accepted: {description}. Error: {result.ErrorMessage}");
        result.NormalizedPath.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ReparsePoint_Junction_Or_Symlink_Is_Blocked()
    {
        var testDir = Path.Combine(TempRoot, "normal_dir_test");
        Directory.CreateDirectory(testDir);
        try
        {
            var isReparse = PathPolicy.IsReparsePoint(testDir);
            isReparse.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(testDir)) Directory.Delete(testDir);
        }
    }

    [Fact]
    public void Real_Junction_Via_Mklink_Is_Blocked()
    {
        if (!OperatingSystem.IsWindows()) return;

        var targetDir = Path.Combine(TempRoot, "real_target_" + Guid.NewGuid().ToString("N"));
        var junctionDir = Path.Combine(TempRoot, "real_junction_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(targetDir);
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junctionDir}\" \"{targetDir}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(5000);

            if (Directory.Exists(junctionDir))
            {
                try
                {
                    PathPolicy.IsReparsePoint(junctionDir).Should().BeTrue("mklink /J creates an NTFS reparse point");

                    var result = _policy.ValidatePath(junctionDir);
                    result.IsValid.Should().BeFalse("Junction folder must be blocked by PathPolicy");
                    result.ErrorMessage.Should().Contain("reparse point");
                }
                finally
                {
                    Directory.Delete(junctionDir);
                }
            }
        }
        finally
        {
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir);
        }
    }

    [Theory]
    [InlineData("Evil")]
    [InlineData("_malicious")]
    [InlineData("-bad")]
    public void Root_Prefix_Confusion_Is_Blocked(string suffix)
    {
        var confusionRoot = TempRoot + suffix;
        var testPath = Path.Combine(confusionRoot, "secret.txt");

        var result = _policy.ValidatePath(testPath);
        result.IsValid.Should().BeFalse($"Path '{testPath}' must be blocked due to root prefix confusion");
        result.ErrorMessage.Should().Contain("outside allowed roots");
    }

    [Fact]
    public void Forward_Slash_Input_Is_Accepted_And_Normalized()
    {
        var forwardSlashPath = TempRoot.Replace('\\', '/') + "/nested/forward_doc.txt";
        var result = _policy.ValidatePath(forwardSlashPath);

        result.IsValid.Should().BeTrue($"Pure forward slash path '{forwardSlashPath}' should be accepted");
        result.NormalizedPath.Should().Be(Path.Combine(TempRoot, "nested", "forward_doc.txt"));
    }

    [Fact]
    public void Case_Differences_Are_Accepted_And_Match_Root()
    {
        var upperPath = TempRoot.ToUpperInvariant() + @"\SUBFOLDER\TEST_CASE.TXT";
        var lowerPath = TempRoot.ToLowerInvariant() + @"\subfolder\test_case.txt";

        var resUpper = _policy.ValidatePath(upperPath);
        resUpper.IsValid.Should().BeTrue();

        var resLower = _policy.ValidatePath(lowerPath);
        resLower.IsValid.Should().BeTrue();
    }
}
