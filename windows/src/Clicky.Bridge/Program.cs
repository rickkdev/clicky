using System.Text.Json;
using System.Text.Json.Serialization;
using Clicky.Bridge;
using Clicky.Pointing;

var line = await Console.In.ReadLineAsync();
if (string.IsNullOrWhiteSpace(line)) return;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

object response;
try
{
    var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, jsonOptions) ?? throw new InvalidOperationException("invalid json-rpc request");
    response = await HandleAsync(request, jsonOptions);
}
catch (Exception ex)
{
    response = new JsonRpcResponse(null, null, new BridgeProtocolError("invalid_request", ex.Message, false));
}

Console.WriteLine(JsonSerializer.Serialize(response, jsonOptions));

static async Task<JsonRpcResponse> HandleAsync(JsonRpcRequest request, JsonSerializerOptions jsonOptions)
{
    try
    {
        var result = request.Method switch
        {
            "rpc.health" => new
            {
                protocolVersion = "clicky.hermes.v1",
                ok = true,
                status = "ok",
                transport = "stdio",
                bridge = "windows"
            },
            "clicky.getCapabilities" => await GetCapabilitiesAsync(request.Params, jsonOptions),
            "clicky.observeScreen" => await ObserveAsync(request.Params, jsonOptions),
            "clicky.explainScreen" => await ExplainScreenAsync(request.Params, jsonOptions),
            "clicky.pointToTarget" => await PointToTargetAsync(request.Params, jsonOptions),
            _ => throw new BridgeProtocolException(new BridgeProtocolError("method_not_found", $"unknown method: {request.Method}", false)),
        };

        return new JsonRpcResponse(request.Id, result, null);
    }
    catch (BridgeProtocolException ex)
    {
        return new JsonRpcResponse(request.Id, null, ex.Error);
    }
}

static async Task<object> GetCapabilitiesAsync(JsonElement? parameters, JsonSerializerOptions jsonOptions)
{
    var available = await Clicky.Capture.ScreenCapturePermissions.ProbeAsync();
    return new
    {
        protocolVersion = "clicky.hermes.v1",
        ok = true,
        status = "ready",
        platform = "windows",
        bridge = new { transport = "stdio", runtime = "windows" },
        capabilities = new
        {
            observeScreen = new { enabled = available, reason = available ? null : "screen_capture_unavailable" },
            explainScreen = new { enabled = available && HasExplanationResponseConfig(), reason = available ? (HasExplanationResponseConfig() ? null : "missing_explanation_response_config") : "screen_capture_unavailable" },
            pointToTarget = new { enabled = available && HasPointingResponseConfig(), reason = available ? (HasPointingResponseConfig() ? null : "missing_pointing_response_config") : "screen_capture_unavailable" },
            overlay = new { enabled = false, reason = "standalone_bridge_overlay_not_available" },
            osControl = new { enabled = false, reason = "phase_2_not_implemented" },
        }
    };
}

static async Task<object> ObserveAsync(JsonElement? parameters, JsonSerializerOptions jsonOptions)
{
    var request = parameters.HasValue
        ? parameters.Value.Deserialize<ObserveScreenRequest>(jsonOptions) ?? new ObserveScreenRequest()
        : new ObserveScreenRequest();
    var service = new WindowsObserveService(new WindowsScreenCaptureProvider());
    return await service.ObserveScreenAsync(request);
}

static async Task<object> ExplainScreenAsync(JsonElement? parameters, JsonSerializerOptions jsonOptions)
{
    var request = parameters.HasValue
        ? parameters.Value.Deserialize<ExplainScreenRequest>(jsonOptions)
        : null;
    if (request is null)
        throw new BridgeProtocolException(new BridgeProtocolError("invalid_request", "explainScreen params are required", false));

    var service = new WindowsExplainScreenService(
        new WindowsScreenCaptureProvider(),
        new EnvironmentExplanationProvider(jsonOptions));
    return await service.ExplainScreenAsync(request);
}

static async Task<object> PointToTargetAsync(JsonElement? parameters, JsonSerializerOptions jsonOptions)
{
    var request = parameters.HasValue
        ? parameters.Value.Deserialize<PointToTargetRequest>(jsonOptions)
        : null;
    if (request is null)
        throw new BridgeProtocolException(new BridgeProtocolError("invalid_request", "pointToTarget params are required", false));

    var service = new WindowsPointToTargetService(
        new WindowsScreenCaptureProvider(),
        new EnvironmentPointingTurnProvider(),
        new NoopPointOverlayRenderer());
    return await service.PointToTargetAsync(request);
}

static bool HasExplanationResponseConfig()
    => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLICKY_BRIDGE_EXPLANATION_RESPONSE"));

static bool HasPointingResponseConfig()
    => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CLICKY_BRIDGE_POINTING_RESPONSE"));

public sealed class EnvironmentExplanationProvider(JsonSerializerOptions jsonOptions) : IWindowsExplanationProvider
{
    public Task<ExplanationResult> ExplainAsync(
        ExplainScreenRequest request,
        IReadOnlyList<Clicky.Capture.CapturedScreen> screens,
        CancellationToken cancellationToken)
    {
        var response = Environment.GetEnvironmentVariable("CLICKY_BRIDGE_EXPLANATION_RESPONSE");
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new BridgeProtocolException(new BridgeProtocolError(
                "bridge_unavailable",
                "CLICKY_BRIDGE_EXPLANATION_RESPONSE is required for standalone bridge explanation. The tray-owned real model/speech/TTS path is not launched implicitly.",
                false));
        }

        var trimmed = response.Trim();
        if (trimmed.StartsWith("{"))
        {
            return Task.FromResult(JsonSerializer.Deserialize<ExplanationResult>(trimmed, jsonOptions)
                ?? throw new BridgeProtocolException(new BridgeProtocolError("invalid_explanation_response", "CLICKY_BRIDGE_EXPLANATION_RESPONSE could not be parsed.", false)));
        }

        return Task.FromResult(ExplanationResult.Text(trimmed));
    }
}

public sealed class EnvironmentPointingTurnProvider : IWindowsPointingTurnProvider
{
    public Task<PointingTurnResult> GetPointingTurnAsync(
        PointToTargetRequest request,
        IReadOnlyList<Clicky.Capture.CapturedScreen> screens,
        CancellationToken cancellationToken)
    {
        if (!request.UseRedesignedPointingProtocol)
        {
            throw new BridgeProtocolException(new BridgeProtocolError(
                "unsupported_protocol",
                "Windows bridge pointToTarget requires useRedesignedPointingProtocol=true.",
                false));
        }

        var response = Environment.GetEnvironmentVariable("CLICKY_BRIDGE_POINTING_RESPONSE");
        if (string.IsNullOrWhiteSpace(response))
        {
            throw new BridgeProtocolException(new BridgeProtocolError(
                "bridge_unavailable",
                "CLICKY_BRIDGE_POINTING_RESPONSE is required for standalone bridge pointing. The tray-owned real model/overlay path is not launched implicitly.",
                false));
        }

        return Task.FromResult(StructuredPointingTurnParser.Parse(response));
    }
}

public sealed record JsonRpcRequest(
    [property: JsonPropertyName("jsonrpc")] string Jsonrpc,
    [property: JsonPropertyName("id")] object? Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement? Params);

public sealed record JsonRpcResponse(
    [property: JsonPropertyName("id")] object? Id,
    [property: JsonPropertyName("result")] object? Result,
    [property: JsonPropertyName("error")] BridgeProtocolError? Error)
{
    [JsonPropertyName("jsonrpc")]
    public string Jsonrpc => "2.0";
}
