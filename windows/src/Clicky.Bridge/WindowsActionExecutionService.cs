namespace Clicky.Bridge;

public interface IWindowsActionExecutor
{
    Task<Dictionary<string, object?>> ClickAsync(ActionProposal proposal, CancellationToken cancellationToken);
    Task<Dictionary<string, object?>> DoubleClickAsync(ActionProposal proposal, CancellationToken cancellationToken);
    Task<Dictionary<string, object?>> TypeTextAsync(ActionProposal proposal, CancellationToken cancellationToken);
    Task<Dictionary<string, object?>> HotkeyAsync(ActionProposal proposal, CancellationToken cancellationToken);
    Task<Dictionary<string, object?>> OpenApplicationAsync(ActionProposal proposal, CancellationToken cancellationToken);
    Task<Dictionary<string, object?>> FocusWindowAsync(ActionProposal proposal, CancellationToken cancellationToken);
}

public sealed class WindowsActionExecutionService
{
    private const string ProtocolVersion = "clicky.hermes.v1";
    private static readonly HashSet<string> SupportedActions = ["click", "doubleClick", "typeText", "hotkey", "openApplication", "focusWindow"];
    private readonly IWindowsActionExecutor _executor;

    public WindowsActionExecutionService(IWindowsActionExecutor executor)
    {
        _executor = executor;
    }

    public async Task<WindowsActionExecutionResult> ExecuteActionAsync(
        WindowsActionExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Timestamp();
        var proposal = request.Proposal;

        if (cancellationToken.IsCancellationRequested)
            return Blocked(proposal, "cancelled", "cancelled before execution", startedAt, forwarded: false);

        var gateFailure = ValidateGates(request);
        if (gateFailure is not null)
            return Blocked(proposal, "blocked", gateFailure, startedAt, forwarded: false);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = proposal.ActionType switch
            {
                "click" => await _executor.ClickAsync(proposal, cancellationToken).ConfigureAwait(false),
                "doubleClick" => await _executor.DoubleClickAsync(proposal, cancellationToken).ConfigureAwait(false),
                "typeText" => await _executor.TypeTextAsync(proposal, cancellationToken).ConfigureAwait(false),
                "hotkey" => await _executor.HotkeyAsync(proposal, cancellationToken).ConfigureAwait(false),
                "openApplication" => await _executor.OpenApplicationAsync(proposal, cancellationToken).ConfigureAwait(false),
                "focusWindow" => await _executor.FocusWindowAsync(proposal, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("unsupported action type"),
            };

            return new WindowsActionExecutionResult(
                ProtocolVersion,
                Ok: true,
                Status: "executed",
                ProposalId: proposal.Id,
                ActionType: proposal.ActionType,
                Result: RedactResult(result),
                Reason: null,
                StartedAt: startedAt,
                CompletedAt: Timestamp(),
                ForwardedToExecutor: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Blocked(proposal, "cancelled", "cancelled during execution", startedAt, forwarded: true);
        }
        catch (Exception ex)
        {
            return Blocked(proposal, "error", ex.Message, startedAt, forwarded: true);
        }
    }

    private static string? ValidateGates(WindowsActionExecutionRequest request)
    {
        var proposal = request.Proposal;
        if (string.IsNullOrWhiteSpace(proposal.Id))
            return "proposal id is required";
        if (!SupportedActions.Contains(proposal.ActionType))
            return "unsupported action type";

        var permission = request.PermissionDecision;
        if (permission is null)
            return "missing permission decision";
        if (permission.ActionType != proposal.ActionType)
            return "permission decision action mismatch";
        if (permission.Decision == "block")
            return "permission decision blocked execution";
        if (permission.Decision is not ("allow" or "requireConfirmation"))
            return "permission decision did not pass";

        var safety = request.SafetyDecision;
        if (safety is null)
            return "missing safety decision";
        if (safety.ProposalId != proposal.Id)
            return "safety decision proposal mismatch";
        if (safety.ActionType != proposal.ActionType)
            return "safety decision action mismatch";
        if (safety.Decision != "allow" || !safety.ForwardToExecutor)
            return "safety decision did not allow forwarding";

        var needsConfirmation = proposal.RequiresConfirmation || permission.Decision == "requireConfirmation";
        if (needsConfirmation)
        {
            var confirmation = request.ConfirmationResponse;
            if (confirmation is null || confirmation.ProposalId != proposal.Id || confirmation.Decision != "approved" || !confirmation.ForwardToExecutor)
                return "approved confirmation required";
            if (confirmation.SafetyDecision is not null && (confirmation.SafetyDecision.Decision != "allow" || !confirmation.SafetyDecision.ForwardToExecutor))
                return "confirmation safety recheck did not allow forwarding";
        }

        return null;
    }

    private static IReadOnlyDictionary<string, object?> RedactResult(Dictionary<string, object?> result)
    {
        result.Remove("text");
        result.Remove("typedText");
        result.Remove("inputPreview");
        return result;
    }

    private static WindowsActionExecutionResult Blocked(
        ActionProposal proposal,
        string status,
        string reason,
        string startedAt,
        bool forwarded)
        => new(
            ProtocolVersion,
            Ok: false,
            Status: status,
            ProposalId: proposal.Id,
            ActionType: proposal.ActionType,
            Result: null,
            Reason: reason,
            StartedAt: startedAt,
            CompletedAt: Timestamp(),
            ForwardedToExecutor: forwarded);

    private static string Timestamp() => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
}
