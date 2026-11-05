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
public sealed class SafetyGate : ISafetyGate
{
    private readonly ToolPolicy _toolPolicy;
    private readonly PathPolicy? _pathPolicy;
    private readonly AuthorizationService _authService;
    private readonly IApprovalHandler? _approvalHandler;
    private readonly IAuditStore? _auditStore;
    private readonly IClock _clock;

    public SafetyGate(
        ToolPolicy toolPolicy,
        AuthorizationService authService,
        IApprovalHandler? approvalHandler = null,
        IAuditStore? auditStore = null,
        IClock? clock = null,
        PathPolicy? pathPolicy = null)
    {
        _toolPolicy = toolPolicy ?? throw new ArgumentNullException(nameof(toolPolicy));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _approvalHandler = approvalHandler;
        _auditStore = auditStore;
        _clock = clock ?? SystemClock.Instance;
        _pathPolicy = pathPolicy;
    }

    public async Task<SafetyEvaluation> EvaluateStepAsync(
        string taskId,
        string stepId,
        string toolId,
        IReadOnlyDictionary<string, object?> rawArgs,
        CancellationToken cancellationToken = default)
    {
        var jsonArgs = new JsonObject();
        foreach (var kv in rawArgs)
        {
            if (kv.Value is string s) jsonArgs[kv.Key] = s;
            else if (kv.Value is bool b) jsonArgs[kv.Key] = b;
            else if (kv.Value is int i) jsonArgs[kv.Key] = i;
            else if (kv.Value is long l) jsonArgs[kv.Key] = l;
            else if (kv.Value is double d) jsonArgs[kv.Key] = d;
            else if (kv.Value != null) jsonArgs[kv.Key] = kv.Value.ToString();
        }

        var validation = _toolPolicy.ValidateToolCall(toolId, jsonArgs);
        if (!validation.IsValid)
        {
            await RecordPolicyValidationAsync(taskId, stepId, toolId, "unknown", "high", "high", false, validation.ErrorMessage, cancellationToken).ConfigureAwait(false);
            return new SafetyEvaluation(false, "high", validation.ErrorMessage);
        }

        if (_pathPolicy != null)
        {
            if (rawArgs.TryGetValue("path", out var pObj) && pObj is string p)
            {
                var pathVal = _pathPolicy.ValidatePath(p);
                if (!pathVal.IsValid)
                {
                    await RecordPolicyValidationAsync(taskId, stepId, toolId, "unknown", "high", "high", false, pathVal.ErrorMessage, cancellationToken).ConfigureAwait(false);
                    return new SafetyEvaluation(false, "high", pathVal.ErrorMessage);
                }
            }
            else if (rawArgs.TryGetValue("root_path", out var rpObj) && rpObj is string rp)
            {
                var pathVal = _pathPolicy.ValidatePath(rp);
                if (!pathVal.IsValid)
                {
                    await RecordPolicyValidationAsync(taskId, stepId, toolId, "unknown", "high", "high", false, pathVal.ErrorMessage, cancellationToken).ConfigureAwait(false);
                    return new SafetyEvaluation(false, "high", pathVal.ErrorMessage);
                }
            }
        }

        var policyRisk = validation.PolicyRisk ?? _toolPolicy.GetPolicyRisk(toolId) ?? "medium";
        await RecordPolicyValidationAsync(taskId, stepId, toolId, policyRisk, policyRisk, policyRisk, true, null, cancellationToken).ConfigureAwait(false);
        var canonicalArgs = jsonArgs.ToDictionary(
            item => item.Key,
            item => item.Value is JsonValue value && value.TryGetValue<string>(out var text) ? (object?)text : item.Value?.ToString(),
            StringComparer.Ordinal);
        return new SafetyEvaluation(true, policyRisk, CanonicalArgs: canonicalArgs);
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
            await RecordPolicyValidationAsync(taskId, stepId, toolId, advisoryRisk, validation.PolicyRisk ?? "unknown", "high", false, validation.ErrorMessage, cancellationToken);
            return SafetyGateResult.Denied(validation.ErrorMessage ?? "Tool policy rejected invocation.");
        }

        var policyRisk = validation.PolicyRisk ?? _toolPolicy.GetPolicyRisk(toolId) ?? "medium";
        var finalRisk = MaxRisk(advisoryRisk, policyRisk);
        await RecordPolicyValidationAsync(taskId, stepId, toolId, advisoryRisk, policyRisk, finalRisk, true, null, cancellationToken).ConfigureAwait(false);

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
        ApprovalDecision decision;
        if (finalRisk == "low")
        {
            decision = new ApprovalDecision(true, "auto-low");
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

            decision = _approvalHandler is null
                ? new ApprovalDecision(false, "timeout", "No approval handler was available.")
                : await _approvalHandler.RequestApprovalAsync(approvalRequest, cancellationToken).ConfigureAwait(false);
        }

        await RecordApprovalDecisionAsync(taskId, stepId, decision, cancellationToken).ConfigureAwait(false);
        if (!decision.Approved)
            return SafetyGateResult.Denied(decision.Reason ?? $"Execution denied by approval handler for step '{stepId}'.");

        // 4. Issue signed AuthorizationRecord
        var record = _authService.IssueAuthorization(
            taskId: taskId,
            stepId: stepId,
            toolId: toolId,
            rawArgs: argsDict,
            risk: finalRisk,
            approvedBy: decision.Approver
        );

        return SafetyGateResult.Success(record);
    }

    private async Task RecordPolicyValidationAsync(
        string taskId, string stepId, string toolId,
        string advisoryRisk, string policyRisk, string finalRisk,
        bool isValid, string? reason,
        CancellationToken cancellationToken)
    {
        if (_auditStore != null)
        {
            await _auditStore.RecordPolicyValidationAsync(new PolicyValidationRecord(
                taskId,
                stepId,
                toolId,
                advisoryRisk,
                policyRisk,
                finalRisk,
                isValid,
                reason,
                _clock.UtcNow
            ), cancellationToken);
        }
    }

    private async Task RecordApprovalDecisionAsync(
        string taskId,
        string stepId,
        ApprovalDecision decision,
        CancellationToken cancellationToken)
    {
        if (_auditStore is not null)
        {
            await _auditStore.RecordApprovalDecisionAsync(new ApprovalDecisionRecord(
                taskId,
                stepId,
                decision.Approved,
                decision.Approver,
                decision.Reason,
                _clock.UtcNow), cancellationToken).ConfigureAwait(false);
        }
    }
}
