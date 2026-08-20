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
/// It derives from <see cref="UnauthorizedAccessException"/> because that is the type clio's auth
/// classifiers key on, and everything decorated here genuinely is a credential rejection (the recorder
/// only decorates the client's <c>"Unauthorized &lt;user&gt; for &lt;url&gt;"</c> shape; every other
/// login failure propagates as its original instance). Four sites depend on it and would silently fall
/// through to their generic arm otherwise: <c>ServerReadinessWaiter</c> (maps it to
/// <c>AuthenticationRejected</c> and fails fast instead of burning the readiness budget on further
/// rejected logins), <c>GetCreatioInfoCommand</c> (<c>BaseProbeFailure.Authentication</c>),
/// <c>SchemaNamePrefixTool</c> (the MCP-visible "Authentication error reading SchemaNamePrefix."
/// result), and <c>SysSettingsCommand.CategorizeError</c>.
/// </para>
/// <para>
/// <b>Rejected alternative:</b> keeping the original as <see cref="Exception.InnerException"/> behind an
/// <c>IAuthoritativeErrorMessage</c> marker does not work. <c>ApplicationSectionCreateCommand</c> reports
/// the root cause via <see cref="Exception.GetBaseException"/><c>.Message</c>, which walks to the
/// inner-most exception and ignores marker interfaces — the diagnostic context would be dropped exactly
/// where it is needed. Deriving keeps both properties at once: inner-most, and correctly classified.
/// </para>
/// <para>
/// The original exception is not lost: its type name and, when it is (or wraps) a
/// <see cref="System.Net.WebException"/>, its transport status are folded into
/// <see cref="Exception.Message"/>, and its full <see cref="object.ToString"/> is stored in
/// <see cref="Exception.Data"/> under <see cref="OriginalExceptionDataKey"/> for a debugger or a
/// verbose log.
/// </para>
/// </remarks>
public sealed class CreatioLoginFailedException : UnauthorizedAccessException {
	#region Constants: Public

	/// <summary>
	/// <see cref="Exception.Data"/> key holding the original exception's
	/// <see cref="object.ToString"/> (type, message, and stack trace).
	/// </summary>
	public const string OriginalExceptionDataKey = "clio.login.originalException";

	/// <summary>
	/// <see cref="Exception.Data"/> key holding the recorded diagnostic context, so a caller can read
	/// the fields structurally instead of parsing them back out of the message.
	/// </summary>
	public const string DiagnosticContextDataKey = "clio.login.diagnostics";

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
