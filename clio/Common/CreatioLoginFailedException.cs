using System;

namespace Clio.Common;

/// <summary>
/// A Creatio login attempt failed. Carries the original failure message plus the diagnostic context
/// recorded by <see cref="ILoginDiagnostics"/>.
/// </summary>
/// <remarks>
/// <para>
/// This exception deliberately has <b>no</b> <see cref="Exception.InnerException"/>. Both message
/// surfaces clio uses walk to the inner-most exception —
/// <see cref="SurfacedExceptionMessage.Resolve"/> at the MCP boundary and
/// <see cref="Exception.GetBaseException"/> in the application-section commands — so a wrapper that
/// kept the original as its inner exception would have its diagnostic message silently discarded,
/// which is the whole point of recording it. Being the inner-most exception is therefore load-bearing:
/// do not add an inner exception here.
/// </para>
/// <para>
/// The original exception is not lost: its type name and, when it is (or wraps) a
/// <see cref="System.Net.WebException"/>, its transport status are folded into
/// <see cref="Exception.Message"/>, and its full <see cref="object.ToString"/> is stored in
/// <see cref="Exception.Data"/> under <see cref="OriginalExceptionDataKey"/> for a debugger or a
/// verbose log.
/// </para>
/// </remarks>
internal sealed class CreatioLoginFailedException : Exception {
	#region Constants: Internal

	/// <summary>
	/// <see cref="Exception.Data"/> key holding the original exception's
	/// <see cref="object.ToString"/> (type, message, and stack trace).
	/// </summary>
	internal const string OriginalExceptionDataKey = "clio.login.originalException";

	/// <summary>
	/// <see cref="Exception.Data"/> key holding the recorded diagnostic context, so a caller can read
	/// the fields structurally instead of parsing them back out of the message.
	/// </summary>
	internal const string DiagnosticContextDataKey = "clio.login.diagnostics";

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Creates a new <see cref="CreatioLoginFailedException"/>.
	/// </summary>
	/// <param name="message">The failure message, already carrying the diagnostic context.</param>
	public CreatioLoginFailedException(string message)
		: base(message) { }

	#endregion
}
