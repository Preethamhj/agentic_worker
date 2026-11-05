using System.Text;

namespace Jarvis.Eval;

/// <summary>Deterministic evaluation-only representation of the post-execution state.</summary>
public sealed record EvalFaultState(
    string ToolId,
    bool ExecutorRan,
    bool ReceiptSuccess,
    bool TargetExists,
    byte[] Content,
    bool SiblingTargetExists,
    bool ExtraFileExists,
    IReadOnlyList<string> ExistingMatches,
    IReadOnlyList<string> ReportedMatches,
    bool AuthorizedProcessRunning,
    bool WrongProcessRunning,
    bool ExtraProcessRunning,
    bool TaskExists,
    bool TaskEnabled,
    int TriggerOffsetSeconds,
    bool ActionMatches,
    bool ExtraTaskExists,
    bool PayloadMatches,
    bool BatterySettingMatches,
    bool ExpirySettingMatches,
    bool EvidenceFresh,
    EvalGroundTruth GroundTruth)
{
    /// <summary>Creates the no-fault state for one supported tool.</summary>
    public static EvalFaultState CreateGood(string toolId) =>
        new(
            toolId,
            true,
            true,
            true,
            Encoding.UTF8.GetBytes("baseline"),
            false,
            false,
            ["match-1", "match-2"],
            ["match-1", "match-2"],
            true,
            false,
            false,
            true,
            true,
            0,
            true,
            false,
            true,
            true,
            true,
            true,
            EvalGroundTruth.Good);
}

/// <summary>Applies protocol faults only to evaluation state; production projects never depend on it.</summary>
public sealed class EvalFaultInjector
{
    /// <summary>Returns the declared fault state or rejects an inapplicable fault/tool pair.</summary>
    public EvalFaultState Inject(string faultId, EvalFaultState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (faultId == "F0")
            return state;
        if (!AppliesTo(faultId, state.ToolId))
            throw new ArgumentException($"Fault '{faultId}' does not apply to tool '{state.ToolId}'.", nameof(faultId));

        var bad = state with { GroundTruth = EvalGroundTruth.Bad };
        return faultId switch
        {
            "F1" => bad with
            {
                ExecutorRan = false,
                ReceiptSuccess = true,
                TargetExists = false,
                ReportedMatches = [],
                AuthorizedProcessRunning = false,
                TaskExists = false
            },
            "F2" => bad with { Content = ChangeFirstByte(state.Content) },
            "F3" => bad with { Content = state.Content[..(state.Content.Length / 2)] },
            "F4" => bad with { TargetExists = false, SiblingTargetExists = true },
            "F5" => bad with { ExtraFileExists = true },
            "F6" => bad with { TargetExists = false },
            "F7" => bad with { ReportedMatches = state.ReportedMatches.Where(path => path != "match-1").ToArray() },
            "F8" => bad with { ReportedMatches = state.ReportedMatches.Append("phantom.txt").ToArray() },
            "F9" => bad with { AuthorizedProcessRunning = false, WrongProcessRunning = true },
            "F10" => bad with { AuthorizedProcessRunning = false },
            "F11" => bad with { ExtraProcessRunning = true },
            "F12" => bad with { TriggerOffsetSeconds = 60 },
            "F13" => bad with { ActionMatches = false },
            "F14" => bad with { TaskEnabled = false },
            "F15" => bad with { ExtraTaskExists = true },
            "F16" => bad with { PayloadMatches = false },
            "F17a" => bad with { BatterySettingMatches = false },
            "F17b" => bad with { ExpirySettingMatches = false },
            "F18" => bad with { EvidenceFresh = false },
            _ => throw new ArgumentException($"Unknown evaluation fault '{faultId}'.", nameof(faultId))
        };
    }

    private static byte[] ChangeFirstByte(byte[] content)
    {
        byte[] changed = content.Length == 0 ? [0x01] : content.ToArray();
        changed[0] ^= 0xff;
        return changed;
    }

    private static bool AppliesTo(string faultId, string toolId) =>
        faultId switch
        {
            "F1" or "F18" => toolId is "fs.create_file" or "fs.create_folder" or "fs.search_files" or "app.launch" or "task.schedule_reminder",
            "F2" or "F3" => toolId == "fs.create_file",
            "F4" or "F5" or "F6" => toolId is "fs.create_file" or "fs.create_folder",
            "F7" or "F8" => toolId == "fs.search_files",
            "F9" or "F10" or "F11" => toolId == "app.launch",
            "F12" or "F13" or "F14" or "F15" or "F16" or "F17a" or "F17b" => toolId == "task.schedule_reminder",
            _ => false
        };
}
