namespace Clio10.Contracts;

/// <summary>An immutable environment snapshot supplied at startup; credentials are not logged.</summary>
public sealed record ClioEnvironment(Uri? BaseUri = null, string UserName = "", string Password = "", bool IsNetCore = true,
    bool AllowUntrustedCertificate = false) {
    /// <summary>Redacts credentials from diagnostic formatting.</summary>
    public override string ToString() => "ClioEnvironment [redacted]";
}

/// <summary>Explicit compatibility requirement; the newest matching local bundle is selected.</summary>
public sealed record PrimitiveRequirement(Version Minimum, Version MaximumExclusive, Version? Exact = null,
    IReadOnlyCollection<string>? Capabilities = null) {
    /// <summary>Checks the declared version and required capabilities without loading implementation code.</summary>
    public bool Matches(Version version, IReadOnlyCollection<string> capabilities) =>
        BundleVersion.Normalize(version) >= BundleVersion.Normalize(Minimum) &&
        BundleVersion.Normalize(version) < BundleVersion.Normalize(MaximumExclusive) &&
        (Exact is null || BundleVersion.Normalize(version) == BundleVersion.Normalize(Exact)) &&
        (Capabilities ?? []).All(capabilities.Contains);
}

/// <summary>Normalizes numeric release identities: omitted build and revision components mean zero.</summary>
public static class BundleVersion {
    /// <summary>Returns the four-component identity used for bundle selection and manifest validation.</summary>
    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
