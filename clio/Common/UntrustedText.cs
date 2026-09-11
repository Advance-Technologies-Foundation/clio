using Clio.Command.McpServer;

namespace Clio.Common;

/// <summary>
/// The <c>Clio.Common</c>-owned seam for neutralizing text a third party authored the CONTENT of -
/// platform validation prose, a login page, a fault envelope, a remote repository's diagnostic.
/// </summary>
/// <remarks>
/// PR #1374 review. Issue #1333 promoted <see cref="SensitiveErrorTextRedactor"/> from an MCP-transport
/// concern to the product-wide untrusted-text rule, so <c>Clio.Common</c> types started importing
/// <c>Clio.Command.McpServer</c> - the shared foundation layer depending on a transport-specific module.
/// Two concrete costs, not stylistic ones: a future non-MCP producer in <c>Common</c> has no
/// discoverable reason to route through a type whose namespace says it only concerns MCP, and
/// <c>Common</c> can no longer be reasoned about or extracted without the MCP module.
/// <para>
/// So this type is the seam <c>Common</c> depends on instead. It is deliberately the only file under
/// <c>clio/Common</c> that reaches into <c>Clio.Command.McpServer</c> for this rule (the separate
/// <c>Clio.Command.McpServer.Progress</c> edge in <c>CreatioUninstaller</c> predates issue #1333 and is
/// untouched here): moving
/// <see cref="SensitiveErrorTextRedactor"/> into <c>Clio.Common</c> is a ~90-file mechanical change
/// deferred out of issue #1333, and when it happens it touches this file rather than every call site.
/// The deferral and its owner are recorded in
/// <c>docs/knowledge/Common/server-prose-in-caller-visible-fields.md</c>.
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
