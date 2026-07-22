namespace Clio.Common;

/// <inheritdoc cref="IRuntimeMode"/>
/// <param name="IsMcpServerMode">
/// <inheritdoc cref="IRuntimeMode.IsMcpServerMode" path="/summary"/>
/// </param>
/// <remarks>
/// A data-only value carrier (DTO). Instances are created directly with <c>new</c> — the record/DTO
/// exemption to CLIO001 — and registered in DI as a singleton instance via the <see cref="IRuntimeMode"/>
/// service type only, so the concrete type stays unregistered.
/// </remarks>
public sealed record RuntimeMode(bool IsMcpServerMode) : IRuntimeMode;
