namespace Clio.Command;

/// <summary>
/// The classified failure of one sys-setting operation, decomposed into the parts a caller can act on.
/// </summary>
/// <remarks>
/// <see cref="Error"/> keeps the exact text the command produced before issue #1329 - the MCP envelope's
/// <c>error</c> field and several tests pin it by equality - so the diagnosis it used to discard now
/// travels beside it instead of replacing it: <see cref="Cause"/> says what happened,
/// <see cref="RecoveryAction"/> says what to do about it, and <see cref="CorrelationId"/> is the token
/// that finds the matching log line.
/// </remarks>
/// <param name="Error">The legacy single-line message, unchanged.</param>
/// <param name="Category">One of <see cref="SysSettingErrorCategories"/>.</param>
/// <param name="Cause">What failed. Fixed local text, except where the cause is already a locally
/// composed diagnostic (a provider failure or an argument rejection).</param>
/// <param name="RecoveryAction">Fixed local text naming the operator's next step.</param>
/// <param name="CorrelationId">The ID shared with the log line written for this failure.</param>
public sealed record SysSettingFailure(
	string Error,
	string Category,
	string Cause,
	string RecoveryAction,
	string CorrelationId);

/// <summary>
/// The <c>error-category</c> values a sys-setting failure envelope can carry. Stable strings: an agent
/// branches on them, so they are part of the tool contract.
/// </summary>
public static class SysSettingErrorCategories {

	/// <summary>The environment rejected the credentials.</summary>
	public const string Authentication = "Authentication";

	/// <summary>The environment could not be reached at all.</summary>
	public const string Network = "Network";

	/// <summary>The data provider reported an unsuccessful response; its message is the diagnosis.</summary>
	public const string ProviderFailure = "ProviderFailure";

	/// <summary>The request was refused by clio before it reached the environment.</summary>
	public const string Validation = "Validation";

	/// <summary>The failure fits none of the above.</summary>
	public const string Unknown = "Unknown";
}

/// <summary>
/// The fixed local texts a sys-setting failure envelope surfaces.
/// </summary>
/// <remarks>
/// Fixed, and local, on purpose. The cause and the recovery action are read by an operator and by an AI
/// agent, so they must not be composed from server-controlled prose - see issue #1333 for the failure
/// mode that rule exists to close.
/// </remarks>
internal static class SysSettingFailureTexts {

	internal const string AuthenticationCause =
		"The environment rejected the credentials of the registered user.";

	internal const string AuthenticationRecovery =
		"Verify the environment credentials (for an expired password, repair the registered profile) and retry.";

	internal const string NetworkCause =
		"The environment could not be reached.";

	internal const string NetworkRecovery =
		"Check the environment URL, network connectivity and the VPN, then retry.";

	internal const string ProviderFailureRecovery =
		"Read the cause, correct the reported condition on the environment, and retry.";

	internal const string ValidationRecovery =
		"Correct the argument named in the cause and call the operation again.";

	internal const string UnknownCause =
		"The operation failed and no cause could be determined from the failure.";

	internal const string UnknownRecovery =
		"Retry the operation; if it fails again, rerun clio with --debug and quote the correlation ID.";
}
