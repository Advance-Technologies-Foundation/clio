using Clio10.PrimitiveContracts;
using System.Text.Json;
using System.Xml;
using Clio10.Contracts;

namespace Clio10.Composition.Services;

/// <summary>Authenticates and executes one application-relative service request without transport retries.</summary>
public abstract class ServiceWorkflow : IClioWorkflow {
    /// <summary>Requires the HTTP capability from a compatible primitives release.</summary>
    public static PrimitiveRequirement Requirement => MaintenanceWorkflow.Requirement;
    /// <summary>Validates and builds the request before authentication or external side effects.</summary>
    protected abstract PrimitiveRequest CreateRequest(IWorkflowContext context);
    /// <summary>Allows workflow-owned input preparation through declared file capabilities.</summary>
    protected virtual Task<PrimitiveRequest> CreateRequestAsync(IWorkflowContext context, CancellationToken cancellationToken) => Task.FromResult(CreateRequest(context));
    /// <summary>Whether the endpoint must return an explicit success envelope.</summary>
    protected virtual bool RequiresSuccess => true;
    /// <summary>Whether the endpoint permits plain text and no-content responses.</summary>
    protected virtual bool AllowsText => false;
    /// <summary>Allows document responses only for probes whose contract is HTTP reachability.</summary>
    protected virtual bool AllowsMarkup => false;
    /// <summary>Allows a reachability probe to recognize redirects without following another origin.</summary>
    protected virtual bool AllowsRedirect => false;
    /// <summary>Marks read-only requests even when transported through POST.</summary>
    protected virtual bool IsRead => false;
    /// <summary>Default deadline of the legacy operation; callers may supply a positive millisecond timeout.</summary>
    protected virtual int DefaultTimeout => 100_000;
    /// <summary>Interprets successful data without changing the transport implementation.</summary>
    protected virtual OperationResult Interpret(OperationResult result, IWorkflowContext context) => result;
    /// <summary>Allows result publication after the service outcome is known.</summary>
    protected virtual Task<OperationResult> InterpretAsync(OperationResult result, string responseBody, IWorkflowContext context, CancellationToken cancellationToken) => Task.FromResult(Interpret(result, context));
    /// <inheritdoc />
    public async Task<OperationResult> ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken) {
        PrimitiveRequest request;
        try {
            long timeout = context.Arguments.TryGetValue("timeout", out var value) ? Convert.ToInt64(value) : DefaultTimeout;
            if (timeout is <= 0 or > int.MaxValue) return new(false, "invalid-arguments");
            request = (await CreateRequestAsync(context, cancellationToken)) with { TimeoutMilliseconds = (int)timeout };
        }
        catch (ArgumentException) { return new(false, "invalid-arguments"); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return new(false, "input-file-unreadable"); }
        if (string.IsNullOrWhiteSpace(context.Environment.UserName) || string.IsNullOrEmpty(context.Environment.Password))
            return new(false, "missing-credentials");
        var primitive = context.Get<IClioPrimitive>();
        context.Report("authenticating");
        var login = await primitive.LoginAsync(cancellationToken);
        var loginError = ServiceResults.LoginError(login);
        if (loginError is not null) return new(false, loginError);
        context.Report("executing");
        var response = await primitive.ExecuteAsync(request, cancellationToken);
        var result = ServiceResults.Read(response, context.PrimitiveVersion, RequiresSuccess, AllowsText,
            request.RelativePath.StartsWith("odata/", StringComparison.OrdinalIgnoreCase) ||
            request.RelativePath.StartsWith("0/odata/", StringComparison.OrdinalIgnoreCase), IsRead || request.Method == "GET", AllowsMarkup, AllowsRedirect);
        return result.Accepted ? await InterpretAsync(result, response.Body, context, cancellationToken) : result;
    }
    /// <summary>Normalizes application aliases; rejects another origin or path traversal.</summary>
    protected static string Route(IWorkflowContext context, string path) {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\') || path.Contains('#'))
            throw new ArgumentException("An application-relative path is required.");
        path = path.TrimStart('/');
        while (path.StartsWith("0/", StringComparison.Ordinal)) path = path[2..];
        var decoded = Uri.UnescapeDataString(path.Split('?')[0]);
        var decodedRequest = Uri.UnescapeDataString(path);
        if (decoded.Contains(':') || decoded.Contains('\\') || decoded.Split('/').Any(x => x is "." or "..") || decoded.StartsWith('/') || path.Length == 0)
            throw new ArgumentException("Invalid service path.");
        if (decodedRequest.Contains('\\') || decodedRequest.Split('/').Contains(".."))
            throw new ArgumentException("Unsupported encoded service path.");
        return (context.Environment.IsNetCore ? "" : "0/") + path;
    }
    /// <summary>Reads a required nonempty textual argument.</summary>
    protected static string Text(IWorkflowContext context, string name) =>
        context.Arguments.TryGetValue(name, out var value) && value is string text && !string.IsNullOrWhiteSpace(text)
            ? text : throw new ArgumentException("Missing argument.");
    /// <summary>Reads an optional boolean, defaulting to false.</summary>
    protected static bool Flag(IWorkflowContext context, string name) => context.Arguments.TryGetValue(name, out var value) && value is true;
}

internal static class ServiceResults {
    internal static string? LoginError(PrimitiveResponse login) {
        if (login.TransportError is not null) return login.TransportError;
        if (login.StatusCode is not (>= 200 and < 300)) return "authentication-http-failure";
        try {
            using var document = JsonDocument.Parse(login.Body);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("Code", out var code) &&
                code.TryGetInt32(out var value) && value == 0 ? null : "authentication-rejected";
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return "invalid-authentication-response"; }
    }
    internal static OperationResult Read(PrimitiveResponse response, Version version, bool requiresSuccess, bool allowsText, bool odata, bool read, bool allowsMarkup, bool allowsRedirect) {
        OperationResult Failure(string code) => new(false, code, PrimitiveVersion: version.ToString());
        if (response.TransportError is not null) return Failure(read || response.TransportError == "authentication-rejected" ? response.TransportError : "outcome-unknown");
        if (allowsRedirect && response.StatusCode is 301 or 302 or 303 or 307 or 308)
            return new(true, "http-reachable", PrimitiveVersion: version.ToString());
        if (response.StatusCode is not (>= 200 and < 300)) return Failure("operation-http-failure") with {
            Payload = new Dictionary<string, object?> { ["statusCode"] = response.StatusCode }
        };
        if (!allowsMarkup && response.Body.TrimStart().StartsWith('<')) {
            if (allowsText && !requiresSuccess && IsXml(response.Body))
                return new(true, "completed", PrimitiveVersion: version.ToString(), Payload: response.Body);
            return Failure("invalid-service-response");
        }
        object? payload;
        try {
            using var document = JsonDocument.Parse(response.Body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object) {
                bool successPresent = Property(root, "success", out var success);
                // OData entity properties are data, not a BaseResponse envelope.
                if (!odata && successPresent && success.ValueKind == JsonValueKind.False) return Failure("service-rejected");
                if (requiresSuccess && (!successPresent || success.ValueKind != JsonValueKind.True)) return Failure("invalid-service-response");
                bool entity = odata && root.TryGetProperty("@odata.context", out _);
                if ((!entity && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object) ||
                    (!odata && root.TryGetProperty("Code", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out int number) && number != 0 && root.TryGetProperty("Exception", out _)) ||
                    (!entity && (root.TryGetProperty("ExceptionType", out _) || root.TryGetProperty("MessageDetail", out _))) ||
                    (odata && !entity && root.EnumerateObject().Count() == 1 && root.TryGetProperty("Message", out _)))
                    return Failure("service-rejected");
            }
            else if (requiresSuccess) return Failure("invalid-service-response");
            payload = Portable(root);
        }
        catch (JsonException) {
            if (!allowsText || requiresSuccess) return Failure("invalid-service-response");
            payload = response.Body;
        }
        return new(true, "completed", PrimitiveVersion: version.ToString(), Payload: payload);
    }
    internal static bool Property(JsonElement value, string name, out JsonElement result) {
        foreach (var property in value.EnumerateObject()) {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { result = property.Value; return true; }
        }
        result = default;
        return false;
    }
    private static bool IsXml(string body) {
        try {
            using var text = new StringReader(body);
            using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName.Equals("html", StringComparison.OrdinalIgnoreCase)) return false;
            while (reader.Read()) { }
            return true;
        }
        catch (XmlException) { return false; }
    }
    internal static object? Portable(JsonElement value) => value.ValueKind switch {
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(x => x.Name, x => Portable(x.Value)),
        JsonValueKind.Array => value.EnumerateArray().Select(Portable).ToArray(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var number) => number,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        JsonValueKind.Number when value.TryGetDouble(out var number) && double.IsFinite(number) => number,
        // Preserve extreme JSON numbers as text instead of leaking infinity into portable results.
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}
