using Clio10.Contracts;
namespace Clio10.PrimitiveContracts;

/// <summary>A low-level HTTP operation. JSON is wire data, not the invocation protocol.</summary>
public sealed record PrimitiveRequest(string Method, string RelativePath, string? Body = null) {
    /// <summary>Positive request timeout in milliseconds; cancellation may end the request sooner.</summary>
    public int TimeoutMilliseconds { get; init; } = 100_000;
    /// <summary>Request bodies can contain secrets and are never included in diagnostic formatting.</summary>
    public override string ToString() => $"PrimitiveRequest {{ Method = {Method}, Body = [redacted] }}";
}

/// <summary>A transport outcome; a received response is not proof of business success.</summary>
public sealed record PrimitiveResponse(int? StatusCode, string Body, string? TransportError = null, string? ProviderVersion = null);

/// <summary>An invocation-scoped session bound to one external target.</summary>
[Capability("http")]
public interface IClioPrimitive {
    /// <summary>Ensures initial authentication once per session. The provider owns expiry recovery.</summary>
    Task<PrimitiveResponse> LoginAsync(CancellationToken cancellationToken);
    /// <summary>Executes without transport retries; the provider may renew expired authentication. Caller cancellation propagates.</summary>
    Task<PrimitiveResponse> ExecuteAsync(PrimitiveRequest request, CancellationToken cancellationToken);
}
