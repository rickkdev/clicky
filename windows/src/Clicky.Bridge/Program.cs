using System.Text.Json;
using System.Text.Json.Serialization;
using Clicky.Bridge;

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
            explainScreen = new { enabled = false, reason = "not_implemented" },
            pointToTarget = new { enabled = false, reason = "not_implemented" },
            overlay = new { enabled = false, reason = "not_implemented" },
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
