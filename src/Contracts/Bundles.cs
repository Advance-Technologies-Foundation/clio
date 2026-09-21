namespace Clio10.Contracts;

/// <summary>Versioned primitive factory. Only this assembly and BCL types cross the load boundary.</summary>
public interface IPrimitiveBundle {
    /// <summary>The bundle release identity, independent of the contract version.</summary>
    Version Version { get; }
    /// <summary>Explicit supported major version of this factory contract.</summary>
    int ContractVersion { get; }
    /// <summary>Capabilities provided by this bundle.</summary>
    IReadOnlyCollection<string> Capabilities { get; }
    /// <summary>Creates an isolated session; the caller owns its disposal.</summary>
    IPrimitiveSession OpenSession(PrimitiveSessionOptions options);
}

/// <summary>A primitive and all resources owned by its operation session.</summary>
public interface IPrimitiveSession : ICapabilityProvider, IDisposable, IAsyncDisposable {
}

/// <summary>Immutable inputs for one session; no Core or transport-library types cross the bundle boundary.</summary>
public sealed record PrimitiveSessionOptions(Uri? BaseUri, string UserName, string Password, bool IsNetCore = true,
    bool AllowUntrustedCertificate = false) {
    /// <summary>Redacts session credentials.</summary>
    public override string ToString() => "PrimitiveSessionOptions [redacted]";
}
