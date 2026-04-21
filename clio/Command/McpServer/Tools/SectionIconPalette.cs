using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using McpServerLib = ModelContextProtocol.Server;
using McpServerType = global::ModelContextProtocol.Server.McpServer;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Canonical 16-color palette that the Creatio <c>SysModule.IconBackground</c> column accepts.
/// Mirrors the palette enforced by <c>DataBindingDomainRules</c> and the 16 swatches rendered in
/// the Freedom UI application/section designer. Any value outside this list is rejected by the
/// backend data binding pipeline.
/// </summary>
internal static class SectionIconPalette {

	/// <summary>
	/// Palette entry with canonical hex value and human-readable English name. The name is
	/// picked to match how the Creatio UI typically labels the swatch; AI callers and users
	/// can refer to the color by hex or by name in multiple languages (see <see cref="Aliases"/>).
	/// </summary>
	internal readonly record struct PaletteEntry(string Hex, string EnglishName);

	/// <summary>
	/// Ordered list of the 16 allowed section/app icon background colors. Order matches the
	/// swatch order shown in the Creatio designer so prompts and AI output stay aligned with
	/// what the human sees in the UI.
	/// </summary>
	internal static readonly IReadOnlyList<PaletteEntry> Entries = [
		new("#A6DE00", "Lime"),
		new("#20A959", "Green"),
		new("#22AC14", "Emerald"),
		new("#FFAC07", "Amber"),
		new("#FF8800", "Orange"),
		new("#F9307F", "Pink"),
		new("#FF602E", "Coral"),
		new("#FF4013", "Red"),
		new("#B87CCF", "Lavender"),
		new("#7848EE", "Violet"),
		new("#247EE5", "Sky"),
		new("#0058EF", "Blue"),
		new("#009DE3", "Cyan"),
		new("#4F43C2", "Indigo"),
		new("#08857E", "Teal"),
		new("#00BFA5", "Mint")
	];

	/// <summary>
	/// Flat list of canonical hex strings for backwards compatibility with callers that
	/// iterate the palette as plain colors.
	/// </summary>
	internal static readonly IReadOnlyList<string> Colors = Entries.Select(e => e.Hex).ToArray();

	private static readonly HashSet<string> NormalizedSet =
		new(Colors, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Case-insensitive dictionary of alternative names → canonical hex. Covers English,
	/// Ukrainian and Russian terms a user may type in a free-form elicitation reply.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, string> Aliases =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["lime"] = "#A6DE00", ["лайм"] = "#A6DE00",
			["green"] = "#20A959", ["зелений"] = "#20A959", ["зеленый"] = "#20A959",
			["emerald"] = "#22AC14", ["смарагд"] = "#22AC14", ["изумруд"] = "#22AC14", ["emerald green"] = "#22AC14",
			["amber"] = "#FFAC07", ["бурштин"] = "#FFAC07", ["янтарь"] = "#FFAC07", ["yellow"] = "#FFAC07", ["жовтий"] = "#FFAC07", ["жёлтый"] = "#FFAC07", ["желтый"] = "#FFAC07",
			["orange"] = "#FF8800", ["помаранчевий"] = "#FF8800", ["оранжевый"] = "#FF8800",
			["pink"] = "#F9307F", ["рожевий"] = "#F9307F", ["розовый"] = "#F9307F", ["magenta"] = "#F9307F",
			["coral"] = "#FF602E", ["корал"] = "#FF602E", ["коралл"] = "#FF602E",
			["red"] = "#FF4013", ["червоний"] = "#FF4013", ["красный"] = "#FF4013",
			["lavender"] = "#B87CCF", ["лаванда"] = "#B87CCF", ["бузковий"] = "#B87CCF", ["сиреневый"] = "#B87CCF",
			["violet"] = "#7848EE", ["purple"] = "#7848EE", ["фіолетовий"] = "#7848EE", ["фиолетовый"] = "#7848EE",
			["sky"] = "#247EE5", ["sky blue"] = "#247EE5", ["небесний"] = "#247EE5", ["небесный"] = "#247EE5", ["голубий"] = "#247EE5", ["голубой"] = "#247EE5",
			["blue"] = "#0058EF", ["синій"] = "#0058EF", ["синий"] = "#0058EF",
			["cyan"] = "#009DE3", ["циан"] = "#009DE3", ["бірюзовий"] = "#009DE3", ["бирюзовый"] = "#009DE3",
			["indigo"] = "#4F43C2", ["індиго"] = "#4F43C2", ["индиго"] = "#4F43C2",
			["teal"] = "#08857E", ["темно-бірюзовий"] = "#08857E", ["тёмно-бирюзовый"] = "#08857E",
			["mint"] = "#00BFA5", ["м'ятний"] = "#00BFA5", ["мятный"] = "#00BFA5"
		};

	/// <summary>
	/// Returns <see langword="true"/> if the supplied color string matches one of the allowed
	/// canonical swatches; otherwise <see langword="false"/>.
	/// </summary>
	internal static bool IsAllowed(string color) =>
		!string.IsNullOrWhiteSpace(color) && NormalizedSet.Contains(color);

	/// <summary>
	/// Tries to translate free-form user input (hex in any case, English/UA/RU color name)
	/// into a canonical palette hex. Returns <see langword="true"/> on a match.
	/// </summary>
	internal static bool TryNormalize(string requested, out string canonical) {
		canonical = null;
		if (string.IsNullOrWhiteSpace(requested)) {
			return false;
		}
		string trimmed = requested.Trim();
		if (NormalizedSet.Contains(trimmed)) {
			canonical = Entries.First(e => string.Equals(e.Hex, trimmed, StringComparison.OrdinalIgnoreCase)).Hex;
			return true;
		}
		if (Aliases.TryGetValue(trimmed, out string mappedHex)) {
			canonical = mappedHex;
			return true;
		}
		return false;
	}

	/// <summary>
	/// Human-readable list of every swatch in the form "Name (#RRGGBB)", comma separated.
	/// Used inside elicitation prompts so the user sees the full palette inline because
	/// Copilot CLI and similar clients render only the prompt string, not schema descriptions.
	/// </summary>
	internal static readonly string InlinePaletteList =
		string.Join(", ", Entries.Select(e => $"{e.EnglishName} ({e.Hex})"));

	/// <summary>
	/// Resolves the icon background color: returns the normalized value when already valid,
	/// elicits a selection from the MCP client when the client declares elicitation support,
	/// or throws an <see cref="ArgumentException"/> with the canonical list otherwise.
	/// </summary>
	internal static async Task<string> ResolveAsync(
		McpServerType server,
		string requestedColor,
		string sectionLabel,
		CancellationToken cancellationToken) {
		if (TryNormalize(requestedColor, out string normalized)) {
			return normalized;
		}

		bool clientSupportsElicitation = server?.ClientCapabilities?.Elicitation is not null;
		if (clientSupportsElicitation) {
			string rejectedLabel = string.IsNullOrWhiteSpace(requestedColor) ? null : requestedColor;
			string prompt = BuildPrompt(rejectedLabel, sectionLabel);
			ElicitResult<SectionIconColorElicitation> result = await server.ElicitAsync<SectionIconColorElicitation>(
				prompt, null, cancellationToken).ConfigureAwait(false);
			if (!result.IsAccepted) {
				throw new InvalidOperationException(
					$"User did not accept the icon background elicitation (action='{result.Action}'). " +
					$"Pass one of: {InlinePaletteList}.");
			}
			SectionIconColorElicitation reply = result.Content;
			if (reply is not null && TryNormalize(reply.IconBackground, out string resolved)) {
				return resolved;
			}
			throw new InvalidOperationException(
				$"Elicitation reply '{reply?.IconBackground}' is not a recognised color. " +
				$"Allowed values: {InlinePaletteList}. Pass either the hex or an English/Ukrainian/Russian name.");
		}

		throw new ArgumentException(
			(string.IsNullOrWhiteSpace(requestedColor)
				? "icon-background is required."
				: $"icon-background '{requestedColor}' is not a recognised color.") +
			$" Allowed values: {InlinePaletteList}. Pass the hex or a color name (EN/UA/RU).");
	}

	private static string BuildPrompt(string requestedColor, string sectionLabel) {
		string label = string.IsNullOrWhiteSpace(sectionLabel) ? "this section" : $"'{sectionLabel}'";
		string intro = string.IsNullOrWhiteSpace(requestedColor)
			? $"Pick an icon background color for {label}."
			: $"The color '{requestedColor}' is not allowed for {label}.";
		return intro + " Creatio accepts one of 16 swatches. " +
			$"Choose: {InlinePaletteList}. Reply with the hex (for example #FF4013) or the color name " +
			"(EN: red, blue, green; UA: червоний, синій, зелений; RU: красный, синий, зелёный).";
	}
}

/// <summary>
/// Structured response shape requested from the MCP client when eliciting a color choice.
/// A single string so clients that render schema-aware forms show a textbox; clients that
/// render only the prompt string still see the full palette inline thanks to
/// <see cref="SectionIconPalette.InlinePaletteList"/>.
/// </summary>
public sealed class SectionIconColorElicitation {

	/// <summary>
	/// The color the user selected. Accepts canonical #RRGGBB hex or an English/UA/RU name
	/// from the Creatio palette. Normalized to a palette hex server-side via
	/// <see cref="SectionIconPalette.TryNormalize"/>.
	/// </summary>
	[JsonPropertyName("iconBackground")]
	[Description(
		"Creatio icon background color. Accepts hex (#RRGGBB) or a color name in EN/UA/RU. " +
		"Allowed hex values: #A6DE00, #20A959, #22AC14, #FFAC07, #FF8800, #F9307F, #FF602E, " +
		"#FF4013, #B87CCF, #7848EE, #247EE5, #0058EF, #009DE3, #4F43C2, #08857E, #00BFA5.")]
	[Required]
	public string IconBackground { get; set; } = string.Empty;
}
