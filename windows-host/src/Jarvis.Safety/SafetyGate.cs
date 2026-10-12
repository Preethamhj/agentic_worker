using System.Text.Json.Nodes;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;

namespace Jarvis.Safety;

public sealed record SafetyGateResult(
    bool Approved,
    AuthorizationRecordDto? Record,
    string? ErrorMessage
)
{
    public static SafetyGateResult Success(AuthorizationRecordDto record) => new(true, record, null);
    public static SafetyGateResult Denied(string message) => new(false, null, message);
}

/// <summary>
/// SafetyGate is the authoritative decision maker for tool execution.
/// Re-validates every tool call independently, enforces policy risk, computes
/// final risk = max(advisory, policy), requests approval via IApprovalProvider,
/// and delegates signing to AuthorizationService.
/// </summary>
public sealed class SafetyGate
{
    private readonly ToolPolicy _toolPolicy;
    private readonly AuthorizationService _authService;
    private readonly IApprovalProvider _approvalProvider;
    private readonly IAuditStore? _auditStore;
    private readonly IClock _clock;

    public SafetyGate(
        ToolPolicy toolPolicy,
        AuthorizationService authService,
        IApprovalProvider approvalProvider,
        IAuditStore? auditStore = null,
        IClock? clock = null)
    {
        _toolPolicy = toolPolicy ?? throw new ArgumentNullException(nameof(toolPolicy));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _approvalProvider = approvalProvider ?? throw new ArgumentNullException(nameof(approvalProvider));
        _auditStore = auditStore;
        _clock = clock ?? SystemClock.Instance;
    }

    /// <summary>
    /// Computes authoritative final risk: max(advisory risk, policy risk).
    /// </summary>
    public static string MaxRisk(string advisoryRisk, string policyRisk)
    {
        var adv = ParseRiskLevel(advisoryRisk);
        var pol = ParseRiskLevel(policyRisk);
        var max = Math.Max(adv, pol);
        return max switch
        {
            3 => "high",
            2 => "medium",
            _ => "low"
        };
    }

    private static int ParseRiskLevel(string? risk) => (risk?.ToLowerInvariant()) switch
    {
        "high" => 3,
        "medium" => 2,
        _ => 1
    };

    public async Task<SafetyGateResult> EvaluateAndAuthorizeAsync(
        string taskId,
        string stepId,
        string toolId,
        JsonNode? rawArgs,
        string advisoryRisk,
        CancellationToken cancellationToken = default)
    {
        // 1. ToolPolicy re-validates tool enabled, schema, and app allowlist
        var validation = _toolPolicy.ValidateToolCall(toolId, rawArgs);
        if (!validation.IsValid)
        {
            await RecordAuditAsync(taskId, stepId, toolId, advisoryRisk, validation.PolicyRisk ?? "unknown", "high", "system", false, validation.ErrorMessage, cancellationToken);
            return SafetyGateResult.Denied(validation.ErrorMessage ?? "Tool policy rejected invocation.");
        }

        var policyRisk = validation.PolicyRisk ?? _toolPolicy.GetPolicyRisk(toolId) ?? "medium";
        var finalRisk = MaxRisk(advisoryRisk, policyRisk);

        // 2. Extract arguments to dictionary
        var argsDict = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (rawArgs is JsonObject obj)
        {
            foreach (var kv in obj)
            {
                if (kv.Value is JsonValue jval)
                {
                    if (jval.TryGetValue<string>(out var s)) argsDict[kv.Key] = s;
                    else if (jval.TryGetValue<bool>(out var b)) argsDict[kv.Key] = b;
                    else if (jval.TryGetValue<long>(out var l)) argsDict[kv.Key] = l;
                    else if (jval.TryGetValue<double>(out var d)) argsDict[kv.Key] = d;
                    else argsDict[kv.Key] = jval.ToString();
                }
                else
                {
                    argsDict[kv.Key] = kv.Value?.ToJsonString();
                }
            }
        }

        // 3. Approval decision based on final authoritative risk
        string approvedBy;
        if (finalRisk == "low")
        {
            approvedBy = "auto";
        }
        else
        {
            var approvalRequest = new ApprovalRequest(
                taskId,
                stepId,
                toolId,
                argsDict,
                advisoryRisk,
                policyRisk,
                finalRisk
            );

            var approved = await _approvalProvider.RequestApprovalAsync(approvalRequest, cancellationToken);
            if (!approved)
            {
                await RecordAuditAsync(taskId, stepId, toolId, advisoryRisk, policyRisk, finalRisk, "user", false, "User denied execution.", cancellationToken);
                return SafetyGateResult.Denied($"Execution denied by approval provider for step '{stepId}'.");
            }

            approvedBy = "user";
        }

        // 4. Issue signed AuthorizationRecord
        var record = _authService.IssueAuthorization(
            taskId: taskId,
            stepId: stepId,
            toolId: toolId,
            rawArgs: argsDict,
            risk: finalRisk,
            approvedBy: approvedBy
        );

        await RecordAuditAsync(taskId, stepId, toolId, advisoryRisk, policyRisk, finalRisk, approvedBy, true, null, cancellationToken);
        return SafetyGateResult.Success(record);
    }

    private async Task RecordAuditAsync(
        string taskId, string stepId, string toolId,
        string advisoryRisk, string policyRisk, string finalRisk,
        string approvedBy, bool approved, string? reason,
        CancellationToken cancellationToken)
    {
        if (_auditStore != null)
        {
            await _auditStore.RecordSafetyDecisionAsync(new SafetyDecisionRecord(
                taskId,
                stepId,
                toolId,
                advisoryRisk,
                policyRisk,
                finalRisk,
                approvedBy,
                approved,
                reason,
                _clock.UtcNow
            ), cancellationToken);
        }
    }
}
