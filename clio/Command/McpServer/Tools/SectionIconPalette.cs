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

	private const string HexLime = "#A6DE00";
	private const string HexGreen = "#20A959";
	private const string HexEmerald = "#22AC14";
	private const string HexAmber = "#FFAC07";
	private const string HexOrange = "#FF8800";
	private const string HexPink = "#F9307F";
	private const string HexCoral = "#FF602E";
	private const string HexRed = "#FF4013";
	private const string HexLavender = "#B87CCF";
	private const string HexViolet = "#7848EE";
	private const string HexSky = "#247EE5";
	private const string HexBlue = "#0058EF";
	private const string HexCyan = "#009DE3";
	private const string HexIndigo = "#4F43C2";
	private const string HexTeal = "#08857E";
	private const string HexMint = "#00BFA5";

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
		new(HexLime, "Lime"),
		new(HexGreen, "Green"),
		new(HexEmerald, "Emerald"),
		new(HexAmber, "Amber"),
		new(HexOrange, "Orange"),
		new(HexPink, "Pink"),
		new(HexCoral, "Coral"),
		new(HexRed, "Red"),
		new(HexLavender, "Lavender"),
		new(HexViolet, "Violet"),
		new(HexSky, "Sky"),
		new(HexBlue, "Blue"),
		new(HexCyan, "Cyan"),
		new(HexIndigo, "Indigo"),
		new(HexTeal, "Teal"),
		new(HexMint, "Mint")
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
			["lime"] = HexLime, ["лайм"] = HexLime,
			["green"] = HexGreen, ["зелений"] = HexGreen, ["зеленый"] = HexGreen,
			["emerald"] = HexEmerald, ["смарагд"] = HexEmerald, ["изумруд"] = HexEmerald, ["emerald green"] = HexEmerald,
			["amber"] = HexAmber, ["бурштин"] = HexAmber, ["янтарь"] = HexAmber, ["yellow"] = HexAmber, ["жовтий"] = HexAmber, ["жёлтый"] = HexAmber, ["желтый"] = HexAmber,
			["orange"] = HexOrange, ["помаранчевий"] = HexOrange, ["оранжевый"] = HexOrange,
			["pink"] = HexPink, ["рожевий"] = HexPink, ["розовый"] = HexPink, ["magenta"] = HexPink,
			["coral"] = HexCoral, ["корал"] = HexCoral, ["коралл"] = HexCoral,
			["red"] = HexRed, ["червоний"] = HexRed, ["красный"] = HexRed,
			["lavender"] = HexLavender, ["лаванда"] = HexLavender, ["бузковий"] = HexLavender, ["сиреневый"] = HexLavender,
			["violet"] = HexViolet, ["purple"] = HexViolet, ["фіолетовий"] = HexViolet, ["фиолетовый"] = HexViolet,
			["sky"] = HexSky, ["sky blue"] = HexSky, ["небесний"] = HexSky, ["небесный"] = HexSky, ["голубий"] = HexSky, ["голубой"] = HexSky,
			["blue"] = HexBlue, ["синій"] = HexBlue, ["синий"] = HexBlue,
			["cyan"] = HexCyan, ["циан"] = HexCyan, ["бірюзовий"] = HexCyan, ["бирюзовый"] = HexCyan,
			["indigo"] = HexIndigo, ["індиго"] = HexIndigo, ["индиго"] = HexIndigo,
			["teal"] = HexTeal, ["темно-бірюзовий"] = HexTeal, ["тёмно-бирюзовый"] = HexTeal,
			["mint"] = HexMint, ["м'ятний"] = HexMint, ["мятный"] = HexMint
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
