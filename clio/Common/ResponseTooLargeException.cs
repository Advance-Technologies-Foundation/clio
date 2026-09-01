using System;

namespace Clio.Common;

/// <summary>
/// Raised when a streamed response reaches its byte ceiling and the transfer is abandoned.
/// </summary>
/// <remarks>
/// Distinct from an ordinary transport failure on purpose: the caller turns this into an actionable
/// message telling the agent to narrow its query, whereas a transport failure means something else went
/// wrong. It also carries the numbers, so the message does not have to be parsed back out of a string.
/// </remarks>
public sealed class ResponseTooLargeException : Exception {

	/// <summary>Creates the exception for a body that reached <paramref name="maxBytes"/>.</summary>
	/// <param name="observedBytes">Bytes received before the transfer was abandoned.</param>
	/// <param name="maxBytes">The ceiling that was reached.</param>
	public ResponseTooLargeException(long observedBytes, long maxBytes)
		: base($"Response is at least {observedBytes} bytes, which exceeds the {maxBytes}-byte limit.") {
		ObservedBytes = observedBytes;
		MaxBytes = maxBytes;
	}

	/// <summary>Gets the number of bytes received before the transfer was abandoned.</summary>
	public long ObservedBytes { get; }

	/// <summary>Gets the ceiling that was reached.</summary>
	public long MaxBytes { get; }
}
