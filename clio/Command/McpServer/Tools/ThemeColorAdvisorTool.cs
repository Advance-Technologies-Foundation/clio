using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Command;
using Clio.Command.Theming;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that answers colour decisions for the interactive theming conversation: it projects the
/// deterministic <c>Clio.Theming</c> engine into pre-computed verdicts (readable or not, too similar or not,
/// valid candidate or not) so the agent never compares a colour metric to a threshold. Read-only, offline,
/// and stateless — the caller re-invokes it whenever a colour input changes. Delegates to
/// <see cref="IThemeColorAdvisor"/>.
/// </summary>
[McpServerToolType]
public sealed class ThemeColorAdvisorTool(IThemeColorAdvisor advisor) {

	internal const string ToolName = "theme-color-advisor";

	/// <summary>Runs one colour-advisory operation and returns its verdict packet.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Colour advisor for the theming palette conversation — returns pre-computed verdicts, not raw numbers. "
		+ "Stateless and offline; re-call it whenever a colour input changes. Select the step via 'operation': "
		+ "triage (sort raw brand colours, pick the primary), adapt-primary (is the primary readable / offer a darker one), "
		+ "derive-secondary (auto secondary + validate an override), accent-evaluate-stored / accent-suggest / accent-validate-manual "
		+ "(the three accent paths), validate-color (check a colour for a role), preview (base -500 per role + system success/error). "
		+ "For the full theme workflow, read get-guidance theming first.")]
	public ThemeColorAdvisorResult Advise(
		[Description("Which step to run: triage | adapt-primary | derive-secondary | accent-evaluate-stored | accent-validate-manual | accent-suggest | validate-color | preview")] [Required] string operation,
		[Description("triage: the raw brand colours the user supplied (any accepted form)")] string[] colors = null,
		[Description("The resolved primary -500. Required for adapt-primary, derive-secondary, accent-* and for validate-color when role=accent")] string primary = null,
		[Description("validate-color: the role to validate against — primary | secondary | accent | success | error")] string role = null,
		[Description("The single raw colour to validate (validate-color, accent-validate-manual)")] string color = null,
		[Description("derive-secondary: an optional secondary override to validate. preview: the fixed secondary -500 anchor")] string secondary = null,
		[Description("preview: the fixed accent -500 anchor")] string accent = null,
		[Description("accent-evaluate-stored: the already-collected candidate hexes to score for similarity to the primary")] string[] candidateHexes = null,
		[Description("preview: system success -500 override; omit to use the template default")] string success = null,
		[Description("preview: system error -500 override; omit to use the template default")] string error = null,
		[Description("preview: offline Creatio template version for the system defaults (e.g. 10.0); the newest bundled version is used when omitted")] string version = null,
		[Description("preview: true returns all 12 palette stops per role; false (default) returns only the base -500 per role")] bool fullStops = false) {
		if (string.IsNullOrWhiteSpace(operation)) {
			return new ThemeColorAdvisorResult { Success = false, Error = "operation is required and cannot be empty." };
		}
		return operation switch {
			"triage" => advisor.Triage(colors),
			"adapt-primary" => advisor.AdaptPrimary(primary),
			"derive-secondary" => advisor.DeriveSecondary(primary, secondary),
			"accent-evaluate-stored" => advisor.EvaluateStoredAccents(primary, candidateHexes),
			"accent-validate-manual" => advisor.ValidateColor("accent", color, primary),
			"accent-suggest" => advisor.SuggestAccents(primary),
			"validate-color" => advisor.ValidateColor(role, color, primary),
			"preview" => advisor.Preview(primary, secondary, accent, success, error, version, fullStops),
			_ => new ThemeColorAdvisorResult { Success = false, Error = $"UNKNOWN_OPERATION: \"{operation}\"" }
		};
	}
}
