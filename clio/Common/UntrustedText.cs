namespace Clio.Common;

/// <summary>
/// The <c>Clio.Common</c>-owned seam for neutralizing text a third party authored the CONTENT of -
/// platform validation prose, a login page, a fault envelope, a remote repository's diagnostic.
/// </summary>
/// <remarks>
/// PR #1374 review. Issue #1333 promoted <see cref="SensitiveErrorTextRedactor"/> from an MCP-transport
/// concern to the product-wide untrusted-text rule; issue #1375 then moved that type into
/// <c>Clio.Common</c>, so the dependency now runs <c>Clio.Command.McpServer</c> -> <c>Clio.Common</c> and
/// never back.
/// <para>
/// This type survives the move because it carries the part the redactor has no opinion about: WHICH of
/// the three renderings a given field takes - fenced for a model-read field, unfenced for a console line,
/// scrub-only for text whose prose clio itself wrote. A call site that picks a redactor method directly
/// records no such reason, so the choice cannot be reviewed later.
/// </para>
/// </remarks>
public static class UntrustedText {

	/// <summary>
	/// The AGENT rendering: scrubbed, flattened, capped and fenced as observed data. Use for an MCP
	/// envelope field, or for a debug line that MCP mode still captures.
	/// </summary>
	/// <param name="text">The raw, possibly attacker-authored text.</param>
	/// <returns>The fenced text, or <see langword="null"/> when there is nothing to report.</returns>
	public static string Fenced(string text) => SensitiveErrorTextRedactor.RedactUntrustedOrNull(text);

	/// <summary>
	/// The CONSOLE rendering: scrubbed, flattened and capped, with no fence. Use for a line whose only
	/// reader is a person at a terminal.
	/// </summary>
	/// <param name="text">The raw, possibly attacker-authored text.</param>
	/// <returns>The console text, or <see langword="null"/> when there is nothing to report.</returns>
	public static string ForConsole(string text) => SensitiveErrorTextRedactor.RedactForConsoleOrNull(text);

	/// <summary>
	/// Replaces known secret shapes - URIs, absolute paths, credential pairs, bearer/JWT tokens,
	/// e-mail addresses - and nothing else. For text whose VALUES a third party influences but whose
	/// prose clio itself wrote.
	/// </summary>
	/// <param name="text">The raw, possibly-sensitive text.</param>
	public static string Scrub(string text) => SensitiveErrorTextRedactor.Redact(text);
}
