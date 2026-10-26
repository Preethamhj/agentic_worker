using System.Text.Json;
using Jarvis.Contracts;
using Jarvis.Safety;
using Jarvis.Tools;

namespace Jarvis.Eval;

/// <summary>Evaluation-only executor decorator that mutates the real post-execution state.</summary>
public sealed class EvalFaultInjectingExecutor(
    IToolExecutor inner,
    string faultId,
    string extraFilePath)
    : IToolExecutor
{
    public string ToolId => inner.ToolId;
    public EvalGroundTruth GroundTruth => faultId == "F0" ? EvalGroundTruth.Good : EvalGroundTruth.Bad;

    public async Task<ExecutionReceiptDto> ExecuteAsync(
        VerifiedAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        if (!IsApplicable(faultId, ToolId))
            throw new ArgumentException($"Fault '{faultId}' is not supported for tool '{ToolId}'.", nameof(faultId));

        var record = authorization.Record;
        var now = DateTimeOffset.UtcNow.ToString("o");
        if (faultId == "F1")
            return SuccessReceipt(record, now);

        if (faultId == "F4")
        {
            var wrongPath = ResolveSiblingPath(record);
            if (ToolId == "fs.create_folder")
                Directory.CreateDirectory(wrongPath);
            else
                await File.WriteAllTextAsync(wrongPath, ReadExpectedContent(record), cancellationToken).ConfigureAwait(false);
            return SuccessReceipt(record, now);
        }

        var receipt = await inner.ExecuteAsync(authorization, cancellationToken).ConfigureAwait(false);
        if (!receipt.ReportedSuccess)
            return receipt;

        if (ToolId is "fs.create_file" or "fs.create_folder")
        {
            var path = ReadPath(record);
            switch (faultId)
            {
                case "F2":
                    var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                    if (bytes.Length == 0)
                        bytes = [0x01];
                    else
                        bytes[0] ^= 0xff;
                    await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
                    break;
                case "F3":
                    var content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(path, content[..(content.Length / 2)], cancellationToken).ConfigureAwait(false);
                    break;
                case "F5":
                    await File.WriteAllTextAsync(extraFilePath, "unauthorized evaluation side effect", cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case "F6":
                    if (ToolId == "fs.create_folder")
                        Directory.Delete(path, recursive: true);
                    else
                        File.Delete(path);
                    break;
            }
        }

        if (ToolId == "fs.search_files" && faultId is "F7" or "F8")
        {
            var reported = JsonSerializer.Deserialize<List<string>>(receipt.Stdout) ?? [];
            if (faultId == "F7" && reported.Count > 0)
                reported.RemoveAt(0);
            if (faultId == "F8")
                reported.Add(Path.Combine(Path.GetTempPath(), $"jarvis-eval-phantom-{Guid.NewGuid():N}.txt"));
            return receipt with { Stdout = JsonSerializer.Serialize(reported) };
        }

        return receipt;
    }

    private static string ReadPath(AuthorizationRecordDto record) =>
        record.CanonicalArgs.TryGetValue("path", out var value) && value is string path
            ? path
            : throw new ArgumentException("Signed authorization has no path.", nameof(record));

    private static string ReadExpectedContent(AuthorizationRecordDto record) =>
        record.CanonicalArgs.TryGetValue("content", out var value) && value is string content
            ? content
            : throw new ArgumentException("Signed authorization has no content.", nameof(record));

    private static string ResolveSiblingPath(AuthorizationRecordDto record)
    {
        var target = ReadPath(record);
        var parent = Path.GetDirectoryName(target)
            ?? throw new ArgumentException("Signed target has no parent directory.", nameof(record));
        return Path.Combine(parent, $"wrong-location-{Path.GetFileName(target)}");
    }

    private static ExecutionReceiptDto SuccessReceipt(AuthorizationRecordDto record, string now) =>
        new(1, record.StepId, record.AuthId, now, now, 0, true, string.Empty, string.Empty);

    private static bool IsApplicable(string faultId, string toolId) =>
        faultId switch
        {
            "F0" or "F1" => true,
            "F2" or "F3" => toolId == "fs.create_file",
            "F5" or "F6" => toolId is "fs.create_file" or "fs.create_folder",
            "F18" => toolId == "fs.create_file",
            "F4" => toolId is "fs.create_file" or "fs.create_folder",
            "F7" or "F8" => toolId == "fs.search_files",
            _ => false
        };
}
