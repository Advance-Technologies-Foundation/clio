namespace Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;

using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>What kind of legacy settings source the merged body is.</summary>
public enum LegacySettingsKind {
	/// <summary>Only what the classic wizard itself writes (the three column buckets).</summary>
	Plain,

	/// <summary>Also carries NON-EMPTY Freedom UI override sections (viewConfigDiff / viewModelConfigDiff / modelConfigDiff / diffV2) — recognised, not converted (ENG-95733). An empty placeholder section does not count.</summary>
	FreedomUiOverrides,

	/// <summary>Carries a hand-authored <c>viewConfig</c> / <c>modelViewConfig</c> — refused: even the classic designer cannot open it.</summary>
	CustomViewConfig
}

/// <summary>
/// A Freedom UI override section found inside legacy settings, with the operations it carries.
/// </summary>
/// <param name="Section">The settings key the section came from.</param>
/// <param name="OperationCount">Operation count, or -1 when the section could not be parsed as an array.</param>
/// <param name="Ticket">The story that owns converting the section, when one does; null once it is converted here.</param>
/// <param name="Supported">Whether the converter processes this section at all.</param>
/// <param name="Reason">Why the section is not processed; null when it is.</param>
/// <param name="Operations">
/// The parsed operations, retained so the rebaser does not have to parse the section a second time. Null when
/// the section is unsupported or could not be parsed — never an empty array standing in for "unknown".
/// </param>
public sealed record LegacyOverrideSection(
	string Section,
	int OperationCount,
	string Ticket,
	bool Supported = false,
	string Reason = null,
	JArray Operations = null);

/// <summary>Static classification of a merged legacy settings node.</summary>
public sealed record LegacySettingsClassification(
	LegacySettingsKind Kind,
	IReadOnlyList<LegacyOverrideSection> OverrideSections,
	IReadOnlyList<string> Notes) {

	/// <summary>Caller-facing label: <c>plain</c> | <c>freedom-ui-overrides</c> | <c>custom-viewconfig</c>.</summary>
	public string Label => Kind switch {
		LegacySettingsKind.Plain => "plain",
		LegacySettingsKind.FreedomUiOverrides => "freedom-ui-overrides",
		_ => "custom-viewconfig"
	};
}

/// <summary>
/// Decides, statically from the merged settings node (never by string-sniffing a body), whether a legacy source
/// is plain wizard output, carries Freedom UI overrides, or carries a custom viewConfig (ENG-95730).
/// </summary>
public static class LegacyMobileSettingsClassifier {

	/// <summary>The story that owns converting embedded Freedom UI overrides.</summary>
	public const string OverridesTicket = "ENG-95733";

	/// <summary>Why <c>diffV2</c> is reported rather than translated.</summary>
	internal const string DiffV2UnsupportedReason =
		"diffV2 is the previous-generation override format. The mobile runtime does not translate it either — its "
		+ "converter registers the inserted names and passes the array through verbatim — so there is no reference "
		+ "behaviour to port, and a translation would be guesswork. Re-author these operations as viewConfigDiff / "
		+ "viewModelConfigDiff / modelConfigDiff on the source schema, or apply them by hand after conversion.";

	private static readonly string[] CustomViewConfigKeys = ["viewConfig", "modelViewConfig"];

	/// <summary>Override sections this converter processes (ENG-95733), in the order the runtime reads them.</summary>
	private static readonly string[] SupportedSectionKeys = ["viewConfigDiff", "viewModelConfigDiff", "modelConfigDiff"];

	/// <summary>Override sections recognised but deliberately NOT processed, each with the reason it is not.</summary>
	private static readonly Dictionary<string, string> UnsupportedSectionKeys =
		new(StringComparer.Ordinal) { ["diffV2"] = DiffV2UnsupportedReason };

	/// <summary>
	/// Classifies the merged settings node.
	/// </summary>
	/// <param name="settings">The merged <c>settings</c> item.</param>
	/// <returns>The classification; a custom viewConfig wins over override sections.</returns>
	public static LegacySettingsClassification Classify(JObject settings) {
		ArgumentNullException.ThrowIfNull(settings);
		var notes = new List<string>();
		var sections = new List<LegacyOverrideSection>();
		foreach (string key in SupportedSectionKeys) {
			if (!IsPresent(settings[key])) {
				continue;
			}
			JArray operations = ReadOperations(settings[key], key, notes);
			int count = operations?.Count ?? -1;
			if (count == 0) {
				// An empty placeholder ("[]" or []) carries nothing to convert — reporting it as a dropped override
				// would tell the user something was lost when nothing was.
				notes.Add($"Override section '{key}' is present but empty; nothing was left unconverted.");
				continue;
			}
			// These sections ARE carried across (ENG-95733), operation by operation, so no ticket is reported for
			// them; what could not be re-pointed comes back per operation in the rebase outcomes. The parsed
			// operations are retained so the rebaser reads exactly what was classified here.
			sections.Add(count < 0
				? new LegacyOverrideSection(key, -1, null, false,
					$"Section '{key}' could not be parsed as a JSON operation array, so none of it can be re-pointed safely.")
				: new LegacyOverrideSection(key, count, null, true, null, operations));
		}
		foreach (KeyValuePair<string, string> unsupported in UnsupportedSectionKeys) {
			if (!IsPresent(settings[unsupported.Key])) {
				continue;
			}
			int count = ReadOperations(settings[unsupported.Key], unsupported.Key, notes)?.Count ?? -1;
			if (count == 0) {
				notes.Add($"Override section '{unsupported.Key}' is present but empty; nothing was left unconverted.");
				continue;
			}
			sections.Add(new LegacyOverrideSection(unsupported.Key, count, null, false, unsupported.Value));
		}
		foreach (string key in CustomViewConfigKeys) {
			if (IsPresent(settings[key])) {
				notes.Add($"Settings carry a hand-authored '{key}' — the classic designer cannot open such a page either.");
				return new LegacySettingsClassification(LegacySettingsKind.CustomViewConfig, sections, notes);
			}
		}
		return new LegacySettingsClassification(
			sections.Count > 0 ? LegacySettingsKind.FreedomUiOverrides : LegacySettingsKind.Plain, sections, notes);
	}

	private static bool IsPresent(JToken token) =>
		token is not null
		&& token.Type is not (JTokenType.Null or JTokenType.Undefined)
		&& !(token.Type == JTokenType.String && string.IsNullOrWhiteSpace(token.Value<string>()));

	/// <summary>
	/// Reads a section's operations. The classic wizard stores override sections as JSON-ENCODED STRINGS (the
	/// mobile runtime parses <c>values.getString(prop)</c>), so a string is parsed first; an already materialized
	/// array is taken as is; anything else yields null and is noted. The parsed array is retained rather than
	/// counted and thrown away, so the rebaser reads the same operations the classification reported on.
	/// </summary>
	private static JArray ReadOperations(JToken token, string key, List<string> notes) {
		switch (token) {
			case JArray array:
				return array;
			case JValue { Type: JTokenType.String } value:
				try {
					if (JToken.Parse(value.Value<string>()) is JArray parsed) {
						return parsed;
					}
					notes.Add($"Override section '{key}' is a string that parses as JSON but not as an operation array; its operations could not be read.");
					return null;
				} catch (Exception) {
					notes.Add($"Override section '{key}' is a string that does not parse as a JSON array; its operations could not be counted.");
					return null;
				}
			default:
				notes.Add($"Override section '{key}' is a {token.Type}; its operations could not be counted.");
				return null;
		}
	}
}
