using System.IO;

namespace Clio.Common;

/// <summary>
/// Raised by <see cref="IConfinedFileAccess.OpenRead"/> when the file is past the caller's byte ceiling and the
/// read is refused before its content is pulled into memory.
/// </summary>
/// <remarks>
/// A dedicated type rather than a plain <see cref="IOException"/> with a recognisable message: the only
/// consumer used to classify the ceiling by a substring of the message, so a reword would silently have
/// demoted "your file is too big" to the generic failure and lost the actionable signal. It derives from
/// <see cref="IOException"/> so a caller that handles I/O failures generically still catches it. It carries the
/// numbers so the message does not have to be parsed back out of a string. The response-side twin is
/// <see cref="ResponseTooLargeException"/>.
/// </remarks>
public sealed class InputFileTooLargeException : IOException {

	/// <summary>Creates the exception for a file that is at least <paramref name="observedBytes"/> long.</summary>
	/// <param name="observedBytes">Size seen so far (the whole length when it was known up front).</param>
	/// <param name="maxBytes">The ceiling that was exceeded.</param>
	public InputFileTooLargeException(long observedBytes, long maxBytes)
		: base($"is at least {observedBytes} bytes, which exceeds the {maxBytes}-byte limit.") {
		ObservedBytes = observedBytes;
		MaxBytes = maxBytes;
	}

	/// <summary>Gets the size seen before the read was refused.</summary>
	public long ObservedBytes { get; }

	/// <summary>Gets the ceiling that was exceeded.</summary>
	public long MaxBytes { get; }
}
