using System.Text.Json.Serialization;

namespace Clicky.Bridge;

public sealed record NativeSelector(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("screen")] string? Screen = null);

public sealed record ActionProposal(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("actionType")] string ActionType,
    [property: JsonPropertyName("targetLabel")] string TargetLabel,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("riskLevel")] string RiskLevel,
    [property: JsonPropertyName("requiresConfirmation")] bool RequiresConfirmation,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("coordinates")] PhysicalPoint? Coordinates = null,
    [property: JsonPropertyName("normalized")] NormalizedPoint? Normalized = null,
    [property: JsonPropertyName("nativeSelector")] NativeSelector? NativeSelector = null,
    [property: JsonPropertyName("inputPreview")] string? InputPreview = null,
    [property: JsonPropertyName("hotkey")] IReadOnlyList<string>? Hotkey = null,
    [property: JsonPropertyName("application")] string? Application = null,
    [property: JsonPropertyName("waitCondition")] string? WaitCondition = null,
    [property: JsonPropertyName("blockedReason")] string? BlockedReason = null);

public sealed record PermissionDecision(
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("permissionTier")] string PermissionTier,
    [property: JsonPropertyName("actionType")] string ActionType,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("requiresExplicitUserSetting")] bool? RequiresExplicitUserSetting = null);

public sealed record SafetyDecision(
    [property: JsonPropertyName("proposalId")] string ProposalId,
    [property: JsonPropertyName("actionType")] string ActionType,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("riskFlags")] IReadOnlyList<string> RiskFlags,
    [property: JsonPropertyName("forwardToExecutor")] bool ForwardToExecutor);

public sealed record ConfirmationResponseState(
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("proposalId")] string ProposalId,
    [property: JsonPropertyName("forwardToExecutor")] bool ForwardToExecutor,
    [property: JsonPropertyName("safetyRechecked")] bool? SafetyRechecked = null,
    [property: JsonPropertyName("safetyDecision")] SafetyDecision? SafetyDecision = null);

public sealed record WindowsActionExecutionRequest(
    [property: JsonPropertyName("proposal")] ActionProposal Proposal,
    [property: JsonPropertyName("permissionDecision")] PermissionDecision? PermissionDecision,
    [property: JsonPropertyName("safetyDecision")] SafetyDecision? SafetyDecision,
    [property: JsonPropertyName("confirmationResponse")] ConfirmationResponseState? ConfirmationResponse = null);

public sealed record WindowsActionExecutionResult(
    [property: JsonPropertyName("protocolVersion")] string ProtocolVersion,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("proposalId")] string ProposalId,
    [property: JsonPropertyName("actionType")] string ActionType,
    [property: JsonPropertyName("result")] IReadOnlyDictionary<string, object?>? Result,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("startedAt")] string StartedAt,
    [property: JsonPropertyName("completedAt")] string CompletedAt,
    [property: JsonPropertyName("forwardedToExecutor")] bool ForwardedToExecutor);
