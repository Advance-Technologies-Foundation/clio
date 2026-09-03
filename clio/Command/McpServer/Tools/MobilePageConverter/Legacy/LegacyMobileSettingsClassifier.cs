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

/// <summary>A Freedom UI override section found inside legacy settings.</summary>
public sealed record LegacyOverrideSection(string Section, int OperationCount, string Ticket);

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

	private static readonly string[] CustomViewConfigKeys = ["viewConfig", "modelViewConfig"];
	private static readonly string[] OverrideSectionKeys = ["viewConfigDiff", "viewModelConfigDiff", "modelConfigDiff", "diffV2"];

	/// <summary>
	/// Classifies the merged settings node.
	/// </summary>
	/// <param name="settings">The merged <c>settings</c> item.</param>
	/// <returns>The classification; a custom viewConfig wins over override sections.</returns>
	public static LegacySettingsClassification Classify(JObject settings) {
		ArgumentNullException.ThrowIfNull(settings);
		var notes = new List<string>();
		var sections = new List<LegacyOverrideSection>();
		foreach (string key in OverrideSectionKeys) {
			if (!IsPresent(settings[key])) {
				continue;
			}
			int count = CountOperations(settings[key], key, notes);
			if (count == 0) {
				// An empty placeholder ("[]" or []) carries nothing to convert — reporting it as a dropped override
				// would tell the user something was lost when nothing was.
				notes.Add($"Override section '{key}' is present but empty; nothing was left unconverted.");
				continue;
			}
			sections.Add(new LegacyOverrideSection(key, count, OverridesTicket));
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
	/// Counts a section's operations. The classic wizard stores override sections as JSON-ENCODED STRINGS (the
	/// mobile runtime parses <c>values.getString(prop)</c>), so a string is parsed before counting; an already
	/// materialized array is counted directly; anything else cannot be counted (-1) and is noted.
	/// </summary>
	private static int CountOperations(JToken token, string key, List<string> notes) {
		switch (token) {
			case JArray array:
				return array.Count;
			case JValue { Type: JTokenType.String } value:
				try {
					return JToken.Parse(value.Value<string>()) is JArray parsed ? parsed.Count : -1;
				} catch (Exception) {
					notes.Add($"Override section '{key}' is a string that does not parse as a JSON array; its operations could not be counted.");
					return -1;
				}
			default:
				notes.Add($"Override section '{key}' is a {token.Type}; its operations could not be counted.");
				return -1;
		}
	}
}
