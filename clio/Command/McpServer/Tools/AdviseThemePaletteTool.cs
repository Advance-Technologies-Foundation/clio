using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Command;
using Clio.Command.Theming;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool that answers colour decisions for the interactive theming conversation: it projects the
/// deterministic <c>Clio.Theming</c> engine into pre-computed verdicts (readable or not, too similar or not,
/// valid candidate or not) so the agent never compares a colour metric to a threshold. Read-only, offline,
/// and stateless — the caller re-invokes it whenever a colour input changes. Delegates to
/// <see cref="IThemePaletteAdvisor"/>.
/// </summary>
[McpServerToolType]
public sealed class AdviseThemePaletteTool(IThemePaletteAdvisor advisor) {

	internal const string ToolName = "advise-theme-palette";

	// Known mis-spellings an LLM tends to emit instead of the kebab-case argument names. Rejected with
	// an actionable rename hint so a camelCase 'candidateHexes' never silently binds to nothing.
	private static readonly Dictionary<string, string> LegacyAliases = new(StringComparer.Ordinal) {
		["candidateHexes"] = "candidate-hexes",
		["candidate_hexes"] = "candidate-hexes",
		["fullStops"] = "full-stops",
		["full_stops"] = "full-stops"
	};

	/// <summary>Runs one colour-advisory operation and returns its verdict packet.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Colour advisor for the theming palette conversation — returns pre-computed verdicts, not raw numbers. "
		+ "Stateless and offline; re-call it whenever a colour input changes. Select the step via 'operation': "
		+ "triage (sort raw brand colours, pick the primary), adapt-primary (is the primary readable / offer a darker one), "
		+ "derive-secondary (auto secondary + validate an override), accent-evaluate-stored / accent-suggest / accent-validate-manual "
		+ "(the three accent paths), validate-color (check a colour for a role), preview (base -500 per role + system success/error). "
		+ "For the theme workflow, read get-guidance theming first.")]
	public ThemePaletteAdvisorResult Advise(
		[Description("Parameters: operation (required), colors, primary, role, color, secondary, accent, " +
			"candidate-hexes, success, error, version, full-stops (all optional; see each operation's needs).")]
		[Required] AdviseThemePaletteArgs args) {
		string? aliasError = McpToolArgumentSupport.BuildLegacyAliasError(
			args.ExtensionData, LegacyAliases, ".",
			"Valid: operation, colors, primary, role, color, secondary, accent, candidate-hexes, success, error, version, full-stops.");
		if (!string.IsNullOrWhiteSpace(aliasError)) {
			return ThemePaletteAdvisorResult.Failure(aliasError);
		}
		if (string.IsNullOrWhiteSpace(args.Operation)) {
			return ThemePaletteAdvisorResult.Failure("operation is required and cannot be empty.");
		}
		return args.Operation switch {
			"triage" => advisor.Triage(args.Colors),
			"adapt-primary" => advisor.AdaptPrimary(args.Primary),
			"derive-secondary" => advisor.DeriveSecondary(args.Primary, args.Secondary),
			"accent-evaluate-stored" => advisor.EvaluateStoredAccents(args.Primary, args.CandidateHexes),
			"accent-validate-manual" => advisor.ValidateColor("accent", args.Color, args.Primary),
			"accent-suggest" => advisor.SuggestAccents(args.Primary),
			"validate-color" => advisor.ValidateColor(args.Role, args.Color, args.Primary),
			"preview" => advisor.Preview(args.Primary, args.Secondary, args.Accent, args.Success, args.Error,
				args.Version, args.FullStops ?? false),
			_ => ThemePaletteAdvisorResult.Failure($"operation \"{args.Operation}\" is not recognized.")
		};
	}
}

/// <summary>
/// MCP arguments for the <c>advise-theme-palette</c> tool.
/// </summary>
public sealed record AdviseThemePaletteArgs(
	[property: JsonPropertyName("operation")]
	[property: Description("Which step to run: triage | adapt-primary | derive-secondary | accent-evaluate-stored | accent-validate-manual | accent-suggest | validate-color | preview.")]
	[property: Required]
	string? Operation = null,

	[property: JsonPropertyName("colors")]
	[property: Description("triage: the raw brand colours the user supplied (any accepted form).")]
	string[]? Colors = null,

	[property: JsonPropertyName("primary")]
	[property: Description("The resolved primary -500. Required for adapt-primary, derive-secondary, accent-* and for validate-color when role=accent.")]
	string? Primary = null,

	[property: JsonPropertyName("role")]
	[property: Description("validate-color: the role to validate against — primary | secondary | accent | success | error.")]
	string? Role = null,

	[property: JsonPropertyName("color")]
	[property: Description("The single raw colour to validate (validate-color, accent-validate-manual).")]
	string? Color = null,

	[property: JsonPropertyName("secondary")]
	[property: Description("derive-secondary: an optional secondary override to validate. preview: the fixed secondary -500 anchor.")]
	string? Secondary = null,

	[property: JsonPropertyName("accent")]
	[property: Description("preview: the fixed accent -500 anchor.")]
	string? Accent = null,

	[property: JsonPropertyName("candidate-hexes")]
	[property: Description("accent-evaluate-stored: the already-collected candidate hexes to score for similarity to the primary.")]
	string[]? CandidateHexes = null,

	[property: JsonPropertyName("success")]
	[property: Description("preview: system success -500 override; omit to use the template default.")]
	string? Success = null,

	[property: JsonPropertyName("error")]
	[property: Description("preview: system error -500 override; omit to use the template default.")]
	string? Error = null,

	[property: JsonPropertyName("version")]
	[property: Description("preview: offline Creatio template version for the system defaults (e.g. 10.0); the newest bundled version is used when omitted.")]
	string? Version = null,

	[property: JsonPropertyName("full-stops")]
	[property: Description("preview: true returns all 12 palette stops per role; false (default) returns only the base -500 per role.")]
	bool? FullStops = null
) {
	/// <summary>Overflow bag for unknown JSON fields; drives the legacy-alias rename hints.</summary>
	[JsonExtensionData]
	public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
