namespace Clio.Common;

/// <summary>
/// Marks an exception whose own <see cref="System.Exception.Message"/> is the curated, caller-facing
/// explanation and must NOT be replaced by an inner exception's message.
/// <para>
/// The MCP boundary unwraps a failure to its inner-most exception so the surfaced detail is the real cause
/// rather than a dispatch wrapper. That unwrapping defeats a deliberately built message: a guard that
/// classifies a response and keeps the parser failure as the inner exception for diagnostics would still
/// surface the raw parser text (ENG-93365). An exception marked with this interface stops the unwrap at
/// itself, so the curated message reaches the caller while the inner exception stays available for logs
/// and debugging.
/// </para>
/// </summary>
public interface IAuthoritativeErrorMessage;
