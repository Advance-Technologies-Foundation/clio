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
	/// Ordered list of the 16 allowed section/app icon background colors in uppercase #RRGGBB form.
	/// Order matches the swatch order shown in the Creatio designer so that AI callers and user
	/// prompts present colors in the same visual sequence.
	/// </summary>
	internal static readonly IReadOnlyList<string> Colors = [
		"#A6DE00", "#20A959", "#22AC14", "#FFAC07",
		"#FF8800", "#F9307F", "#FF602E", "#FF4013",
		"#B87CCF", "#7848EE", "#247EE5", "#0058EF",
		"#009DE3", "#4F43C2", "#08857E", "#00BFA5"
	];

	/// <summary>
	/// Case-insensitive lookup so AI callers can pass lowercase hex and still pass validation.
	/// </summary>
	private static readonly HashSet<string> NormalizedSet = new(Colors, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Returns <see langword="true"/> if the supplied color string matches one of the allowed
	/// swatches; otherwise <see langword="false"/>.
	/// </summary>
	internal static bool IsAllowed(string color) =>
		!string.IsNullOrWhiteSpace(color) && NormalizedSet.Contains(color);

	/// <summary>
	/// Resolves the icon background color: returns the normalized value when already valid,
	/// elicits a selection from the MCP client when the client declares elicitation support,
	/// or throws an <see cref="ArgumentException"/> with the canonical list otherwise.
	/// </summary>
	/// <param name="server">Active MCP server used to send the elicitation request.</param>
	/// <param name="requestedColor">Optional color supplied by the caller.</param>
	/// <param name="sectionLabel">Short label (for example the section caption) used in the prompt.</param>
	/// <param name="cancellationToken">Cancellation token propagated into the elicitation call.</param>
	/// <returns>A #RRGGBB string that is guaranteed to be a member of <see cref="Colors"/>.</returns>
	internal static async Task<string> ResolveAsync(
		McpServerType server,
		string requestedColor,
		string sectionLabel,
		CancellationToken cancellationToken) {
		if (IsAllowed(requestedColor)) {
			return NormalizeCasing(requestedColor);
		}

		bool clientSupportsElicitation = server?.ClientCapabilities?.Elicitation is not null;
		if (clientSupportsElicitation) {
			string prompt = BuildPrompt(requestedColor, sectionLabel);
			try {
				ElicitResult<SectionIconColorElicitation> result = await server.ElicitAsync<SectionIconColorElicitation>(
					prompt, null, cancellationToken)
					.ConfigureAwait(false);
				if (!result.IsAccepted) {
					throw new InvalidOperationException(
						$"User did not accept the icon background elicitation (action='{result.Action}'). " +
						$"Pass a valid #RRGGBB value from: {string.Join(", ", Colors)}.");
				}
				SectionIconColorElicitation reply = result.Content;
				if (reply is not null && IsAllowed(reply.IconBackground)) {
					return NormalizeCasing(reply.IconBackground);
				}
				throw new InvalidOperationException(
					"Elicitation returned a color outside the allowed SysModule.IconBackground palette: " +
					$"'{reply?.IconBackground}'. Allowed values: {string.Join(", ", Colors)}.");
			} catch (OperationCanceledException) {
				throw;
			} catch (Exception ex) {
				throw new InvalidOperationException(
					$"Failed to elicit icon background color from the MCP client: {ex.Message}. " +
					$"Pass a valid #RRGGBB value from: {string.Join(", ", Colors)}.",
					ex);
			}
		}

		throw new ArgumentException(
			(string.IsNullOrWhiteSpace(requestedColor)
				? "icon-background is required."
				: $"icon-background '{requestedColor}' is not in the allowed SysModule palette.") +
			$" Allowed values: {string.Join(", ", Colors)}.");
	}

	private static string NormalizeCasing(string color) {
		foreach (string canonical in Colors) {
			if (string.Equals(canonical, color, StringComparison.OrdinalIgnoreCase)) {
				return canonical;
			}
		}
		return color;
	}

	private static string BuildPrompt(string requestedColor, string sectionLabel) {
		string label = string.IsNullOrWhiteSpace(sectionLabel) ? "section" : $"'{sectionLabel}'";
		if (string.IsNullOrWhiteSpace(requestedColor)) {
			return $"Pick an icon background color for {label}. Creatio accepts one of 16 predefined swatches.";
		}
		return $"The color '{requestedColor}' is not in the allowed {label} icon palette. " +
			"Creatio accepts one of 16 predefined swatches — pick a valid one.";
	}
}

/// <summary>
/// Structured response shape requested from the MCP client when eliciting a color choice.
/// Exposes a single string property so the client can render a dropdown / swatch picker
/// populated from <see cref="SectionIconPalette.Colors"/>.
/// </summary>
public sealed class SectionIconColorElicitation {

	/// <summary>
	/// The #RRGGBB color the end user selected. Must be a member of <see cref="SectionIconPalette.Colors"/>.
	/// </summary>
	[JsonPropertyName("iconBackground")]
	[Description(
		"Creatio section icon background color in #RRGGBB format. Allowed values: #A6DE00, #20A959, #22AC14, #FFAC07, " +
		"#FF8800, #F9307F, #FF602E, #FF4013, #B87CCF, #7848EE, #247EE5, #0058EF, #009DE3, #4F43C2, #08857E, #00BFA5.")]
	[Required]
	public string IconBackground { get; set; } = string.Empty;
}
