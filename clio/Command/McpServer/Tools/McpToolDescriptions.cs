namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared, terse <c>[Description]</c> text for the parameters that repeat across nearly every MCP
/// tool (the connection arguments). These are compile-time <c>const</c> strings so they can be used
/// directly in <c>[Description(...)]</c> attributes.
/// </summary>
/// <remarks>
/// <para>
/// Referencing one constant does NOT shrink the serialized <c>tools/list</c> payload — every tool
/// still emits its own copy of the string. The win comes from the strings being SHORT: the
/// connection-argument prose was previously inlined verbosely on each tool (the <c>uri</c> blurb
/// alone repeated 11×, the credential blurb 36×, the <c>environment-name</c> description ~184×).
/// Centralizing the terse form here keeps them consistent and prevents the verbose forms creeping
/// back in.
/// </para>
/// <para>
/// Full per-command argument contracts (defaults, required flags, enums) are served on demand by
/// <c>get-tool-contract</c>; these descriptions are intentionally minimal.
/// </para>
/// </remarks>
internal static class McpToolDescriptions {
	/// <summary>Terse description for the <c>environment-name</c> connection argument.</summary>
	internal const string EnvironmentName = "Registered clio environment name. Preferred.";

	/// <summary>Terse description for the <c>uri</c> direct-connection argument.</summary>
	internal const string Uri = "Direct Creatio URL; emergency/bootstrap fallback. Prefer environment-name.";

	/// <summary>Terse description for the <c>login</c> direct-connection argument.</summary>
	internal const string Login = "Direct login paired with uri; fallback only.";

	/// <summary>Terse description for the <c>password</c> direct-connection argument.</summary>
	internal const string Password = "Direct password paired with uri; fallback only.";

	/// <summary>
	/// Terse description for the page <c>resources</c> argument (localizable string key/value pairs).
	/// The full contract — which keys to pass, the no-inline-literals rule, and auto-provided DS-bound
	/// captions — lives in the <c>page-schema-resources</c> guidance.
	/// </summary>
	internal const string PageResources =
		"JSON object string of localizable string key/value pairs the platform does NOT auto-provide " +
		"(custom titles, button captions, validator messages, explicit caption overrides). Omit keys that " +
		"match an existing DS-bound attribute (auto-provided). Inline placeholder/label/caption/title/tooltip " +
		"literals in the body are REJECTED — bind via $Resources.Strings.<Key> and register the key here " +
		"(an unregistered inserted widget/metric title is REJECTED too), except for the rare controls whose " +
		"property does not read a resource (e.g. crt.ImageInput.tooltip, which must stay a literal). " +
		"See get-guidance `page-schema-resources` for the full rule.";
}
