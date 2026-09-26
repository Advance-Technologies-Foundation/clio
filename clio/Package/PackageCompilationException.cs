using System;

namespace Clio.Package;

/// <summary>
/// Raised when Creatio reports that a package build failed, for example on a C# compile error.
/// </summary>
/// <remarks>
/// The diagnostics themselves are written to the log before this is thrown, one line each; the message
/// carries only the summary so a caller that prints it does not repeat them.
/// </remarks>
public sealed class PackageCompilationException : Exception {

	/// <summary>
	/// Initializes a new instance of the <see cref="PackageCompilationException"/> class.
	/// </summary>
	/// <param name="message">The failure summary.</param>
	public PackageCompilationException(string message) : base(message) { }

	/// <summary>
	/// Initializes a new instance of the <see cref="PackageCompilationException"/> class.
	/// </summary>
	/// <param name="message">The failure summary.</param>
	/// <param name="innerException">The underlying cause.</param>
	public PackageCompilationException(string message, Exception innerException) : base(message, innerException) { }

}

/// <summary>
/// Asks a package build to block until the environment has finished building, not merely accepted the request.
/// </summary>
/// <param name="Timeout">How long to wait for the build to finish before giving up.</param>
public sealed record PackageCompilationWaitOptions(TimeSpan Timeout);
