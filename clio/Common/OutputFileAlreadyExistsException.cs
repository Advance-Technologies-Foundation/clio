using System.IO;

namespace Clio.Common;

/// <summary>
/// Raised by a confined writer when the output file's name was already taken at the moment of publish, so the
/// write was refused rather than overwriting existing content.
/// </summary>
/// <remarks>
/// A dedicated type rather than a plain <see cref="IOException"/> with a recognisable message: the consumer
/// used to recognise the collision by a substring of that message, which meant a reword in any one of the
/// platform writers would silently have reclassified "this name is taken" as a transport failure - reported as
/// retryable, although a retry against the same path can never succeed. It derives from
/// <see cref="IOException"/> so a caller that handles I/O failures generically still catches it, and it carries
/// the path so the message does not have to be parsed back out of a string. The size-ceiling twin is
/// <see cref="InputFileTooLargeException"/>.
/// </remarks>
public sealed class OutputFileAlreadyExistsException : IOException {

	/// <summary>Creates the exception for a name that was already taken.</summary>
	/// <param name="path">The output path that could not be published, as it is reported to the caller.</param>
	public OutputFileAlreadyExistsException(string path)
		: base($"output-file '{path}' already exists; refusing to overwrite it. Choose a different "
			+ "path or remove the existing file.") {
		Path = path;
	}

	/// <summary>Gets the output path that was already taken.</summary>
	public string Path { get; }
}
