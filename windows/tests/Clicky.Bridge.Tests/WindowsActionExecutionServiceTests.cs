using Clicky.Bridge;
using Xunit;

namespace Clicky.Tests;

public sealed class WindowsActionExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAction_RefusesMissingPermissionDecision()
    {
        var service = new WindowsActionExecutionService(new RecordingWindowsActionExecutor());

        var result = await service.ExecuteActionAsync(new WindowsActionExecutionRequest(
            Proposal: Proposal("p-1", "click"),
            PermissionDecision: null,
            SafetyDecision: Safety("p-1", "click", "allow", true),
            ConfirmationResponse: null));

        Assert.False(result.Ok);
        Assert.Equal("blocked", result.Status);
        Assert.Equal("missing permission decision", result.Reason);
    }

    [Fact]
    public async Task ExecuteAction_RefusesBlockedSafetyDecision()
    {
        var executor = new RecordingWindowsActionExecutor();
        var service = new WindowsActionExecutionService(executor);

        var result = await service.ExecuteActionAsync(new WindowsActionExecutionRequest(
            Proposal: Proposal("p-1", "click"),
            PermissionDecision: Permission("click", "allow"),
            SafetyDecision: Safety("p-1", "click", "block", false),
            ConfirmationResponse: null));

        Assert.False(result.Ok);
        Assert.Equal("blocked", result.Status);
        Assert.Equal("safety decision did not allow forwarding", result.Reason);
        Assert.Empty(executor.Calls);
    }

    [Fact]
    public async Task ExecuteAction_RefusesUnapprovedConfirmationWhenRequired()
    {
        var executor = new RecordingWindowsActionExecutor();
        var service = new WindowsActionExecutionService(executor);

        var result = await service.ExecuteActionAsync(new WindowsActionExecutionRequest(
            Proposal: Proposal("p-1", "typeText"),
            PermissionDecision: Permission("typeText", "requireConfirmation"),
            SafetyDecision: Safety("p-1", "typeText", "allow", true),
            ConfirmationResponse: null));

        Assert.False(result.Ok);
        Assert.Equal("blocked", result.Status);
        Assert.Equal("approved confirmation required", result.Reason);
        Assert.Empty(executor.Calls);
    }

    [Theory]
    [InlineData("click")]
    [InlineData("doubleClick")]
    [InlineData("typeText")]
    [InlineData("hotkey")]
    [InlineData("openApplication")]
    [InlineData("focusWindow")]
    public async Task ExecuteAction_RoutesAllowedActionsThroughExecutor(string actionType)
    {
        var executor = new RecordingWindowsActionExecutor();
        var service = new WindowsActionExecutionService(executor);

        var result = await service.ExecuteActionAsync(new WindowsActionExecutionRequest(
            Proposal: Proposal("p-1", actionType),
            PermissionDecision: Permission(actionType, "allow"),
            SafetyDecision: Safety("p-1", actionType, "allow", true),
            ConfirmationResponse: null));

        Assert.True(result.Ok);
        Assert.Equal("executed", result.Status);
        Assert.Equal("p-1", result.ProposalId);
        Assert.Equal(actionType, result.ActionType);
        Assert.True(result.ForwardedToExecutor);
        Assert.NotNull(result.StartedAt);
        Assert.NotNull(result.CompletedAt);
        Assert.Contains(actionType, executor.Calls);
    }

    [Fact]
    public async Task ExecuteAction_CancellationTokenReturnsCancelledResultWithoutExecutorSideEffect()
    {
        var executor = new RecordingWindowsActionExecutor();
        var service = new WindowsActionExecutionService(executor);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.ExecuteActionAsync(new WindowsActionExecutionRequest(
            Proposal: Proposal("p-1", "click"),
            PermissionDecision: Permission("click", "allow"),
            SafetyDecision: Safety("p-1", "click", "allow", true),
            ConfirmationResponse: null), cts.Token);

        Assert.False(result.Ok);
        Assert.Equal("cancelled", result.Status);
        Assert.False(result.ForwardedToExecutor);
        Assert.Equal("cancelled before execution", result.Reason);
        Assert.Empty(executor.Calls);
    }

    private static ActionProposal Proposal(string id, string actionType) => new(
        Id: id,
        ActionType: actionType,
        TargetLabel: "safe target",
        Confidence: 0.99,
        RiskLevel: "low",
        RequiresConfirmation: false,
        Rationale: "test proposal",
        Coordinates: new PhysicalPoint(100, 200, "display-1"),
        InputPreview: actionType == "typeText" ? "benign text" : null,
        Hotkey: actionType == "hotkey" ? ["Ctrl", "L"] : null,
        Application: actionType == "openApplication" ? "notepad.exe" : null,
        NativeSelector: actionType == "focusWindow" ? new NativeSelector("windowTitle", "Untitled - Notepad") : null);

    private static PermissionDecision Permission(string actionType, string decision) => new(
        Decision: decision,
        PermissionTier: "confirmBeforeAction",
        ActionType: actionType,
        Reason: "test permission");

    private static SafetyDecision Safety(string proposalId, string actionType, string decision, bool forward) => new(
        ProposalId: proposalId,
        ActionType: actionType,
        Decision: decision,
        Reason: "test safety",
        RiskFlags: [],
        ForwardToExecutor: forward);

    private sealed class RecordingWindowsActionExecutor : IWindowsActionExecutor
    {
        public List<string> Calls { get; } = [];

        public Task<Dictionary<string, object?>> ClickAsync(ActionProposal proposal, CancellationToken cancellationToken)
        {
            Calls.Add("click");
            return Task.FromResult(new Dictionary<string, object?> { ["method"] = "click" });
        }

        public Task<Dictionary<string, object?>> DoubleClickAsync(ActionProposal proposal, CancellationToken cancellationToken)
        {
            Calls.Add("doubleClick");
            return Task.FromResult(new Dictionary<string, object?> { ["method"] = "doubleClick" });
        }

        public Task<Dictionary<string, object?>> TypeTextAsync(ActionProposal proposal, CancellationToken cancellationToken)
        {
            Calls.Add("typeText");
            return Task.FromResult(new Dictionary<string, object?> { ["method"] = "typeText", ["redactedText"] = true });
        }

        public Task<Dictionary<string, object?>> HotkeyAsync(ActionProposal proposal, CancellationToken cancellationToken)
        {
            Calls.Add("hotkey");
            return Task.FromResult(new Dictionary<string, object?> { ["method"] = "hotkey" });
        }

        public Task<Dictionary<string, object?>> OpenApplicationAsync(ActionProposal proposal, CancellationToken cancellationToken)
        {
            Calls.Add("openApplication");
            return Task.FromResult(new Dictionary<string, object?> { ["method"] = "openApplication" });
        }

        public Task<Dictionary<string, object?>> FocusWindowAsync(ActionProposal proposal, CancellationToken cancellationToken)
        {
            Calls.Add("focusWindow");
            return Task.FromResult(new Dictionary<string, object?> { ["method"] = "focusWindow" });
        }
    }
}
