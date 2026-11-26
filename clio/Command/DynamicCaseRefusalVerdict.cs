namespace Clio.Command;

/// <summary>
/// The verdict line a dynamic-case command prints for a refusal. The refusal's reason and remedy travel once, as the
/// result document's <c>errorMessage</c>; the verdict names the stable code and points at it, so the Error line and the
/// document never carry the same text twice.
/// </summary>
internal static class DynamicCaseRefusalVerdict {

	/// <summary>The verdict of a refusal.</summary>
	/// <param name="errorCode">The result document's stable <c>errorCode</c>.</param>
	/// <param name="errorMessage">The result document's <c>errorMessage</c>; only whether it is blank is read.</param>
	/// <param name="writeState">What the call left written, as a sentence with a leading space (for example
	/// " Nothing was written."); empty for a read.</param>
	internal static string For(string errorCode, string errorMessage, string writeState) =>
		$"Refused [{errorCode}]"
		+ (string.IsNullOrWhiteSpace(errorMessage)
			? "."
			: "; the result's errorMessage says why and what to do next.")
		+ writeState;
}
