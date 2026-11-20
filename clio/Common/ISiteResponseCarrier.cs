namespace Clio.Common;

/// <summary>
/// A typed MCP tool result that carries <c>site-response</c> when it reports a failure.
/// </summary>
/// <remarks>
/// The tool base opens a <see cref="SiteResponseCapture"/> around every typed-result call and, when the result
/// implements this interface, reports a failure and has no site response yet, sets the last refusal recorded
/// during the call and, beside it, the <c>diagnostic</c> whose side effect says whether that refusal proves the
/// call applied nothing. A result that does not implement it is returned untouched.
/// </remarks>
public interface ISiteResponseCarrier {

	/// <summary>Whether the result reports that the call failed.</summary>
	bool ReportsFailure { get; }

	/// <summary>What the site itself answered to the call that failed, or <see langword="null"/>.</summary>
	SiteResponse SiteResponse { get; set; }

	/// <summary>
	/// The side effect of the refused call beside <see cref="SiteResponse"/>, or <see langword="null"/> when there
	/// is no site response.
	/// </summary>
	DataWriteDiagnostic Diagnostic { get; set; }

}
