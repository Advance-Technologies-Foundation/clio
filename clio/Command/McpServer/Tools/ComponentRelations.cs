using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// A "see also" suggestion attached to a <c>get-component-info</c> detail response, pointing the
/// agent at a better-fit component it is likely to overlook for the requested type.
/// </summary>
/// <param name="ComponentType">The suggested Freedom UI component type (e.g. <c>crt.Gallery</c>).</param>
/// <param name="Reason">A short, agent-facing rationale for why this alternative is worth evaluating.</param>
public sealed record RelatedComponentSuggestion(
	[property: JsonPropertyName("componentType")] string ComponentType,
	[property: JsonPropertyName("reason")] string Reason);

/// <summary>
/// Curated, decision-point "see also" map for the <c>get-component-info</c> detail response
/// (ENG-91574 / ENG-91134 root cause). When an agent fetches the detail for a collection/visual
/// component it has already settled on (for example <c>crt.DataGrid</c> or <c>crt.ImageInput</c>),
/// the response carries a <see cref="RelatedComponentSuggestion"/> nudging it toward a better-fit
/// component it routinely overlooks (today: <c>crt.Gallery</c>). This hits the call the agent
/// actually makes, rather than the startup instructions it skips.
/// </summary>
/// <remarks>
/// This is a small, hand-curated clio-side map — deliberately NOT derived from the registry — so the
/// nudge works even before the producer-side selection metadata (ENG-91571) lands. It is intentionally
/// conservative: only the well-known confusable collection/visual types carry a suggestion, so the
/// signal stays low-noise. Once the registry ships richer selection metadata, this map can be folded
/// into a data-driven relationship once that data exists.
/// </remarks>
public static class ComponentRelations {

	/// <summary>
	/// Stateless discovery breadcrumb attached to every detail response. The reopened ENG-91134
	/// root cause was the agent authoring component choices from memory without ever listing the
	/// catalog; this tip — present on every single-component detail response, requiring no
	/// per-session state — steers it back to list-mode discovery so non-obvious components are not
	/// missed.
	/// </summary>
	public const string DiscoveryTip =
		"This is a single component (detail mode). Before committing to a layout, call get-component-info "
		+ "in list mode (omit component-type) to review the full catalog — non-obvious components such as "
		+ "crt.Gallery are easy to miss when authoring from memory.";

	private const string GalleryReason =
		"crt.Gallery renders a card/tile collection of records or images (photo grids, catalogs, "
		+ "previews). Evaluate it for image or card collections before settling on a tabular grid or a "
		+ "file-attachment list.";

	private static readonly IReadOnlyList<RelatedComponentSuggestion> SuggestGallery =
		new[] { new RelatedComponentSuggestion("crt.Gallery", GalleryReason) };

	/// <summary>
	/// Maps a collection/visual component type to the alternatives an agent should consider before
	/// committing to it. Keyed case-insensitively on <c>componentType</c>.
	/// </summary>
	private static readonly IReadOnlyDictionary<string, IReadOnlyList<RelatedComponentSuggestion>> RelatedByType =
		new Dictionary<string, IReadOnlyList<RelatedComponentSuggestion>>(StringComparer.OrdinalIgnoreCase) {
			["crt.DataGrid"] = SuggestGallery,
			["crt.List"] = SuggestGallery,
			["crt.FileList"] = SuggestGallery,
			["crt.MultiList"] = SuggestGallery,
			["crt.ImageInput"] = SuggestGallery
		};

	/// <summary>
	/// Returns the curated "see also" suggestions for the given component type, or <c>null</c> when
	/// the type has no curated alternatives (so the response omits the field entirely).
	/// </summary>
	/// <param name="componentType">The Freedom UI component type being detailed.</param>
	/// <returns>The suggestions for <paramref name="componentType"/>, or <c>null</c>.</returns>
	public static IReadOnlyList<RelatedComponentSuggestion>? GetRelated(string? componentType) {
		if (string.IsNullOrWhiteSpace(componentType)) {
			return null;
		}
		return RelatedByType.TryGetValue(componentType, out IReadOnlyList<RelatedComponentSuggestion>? related)
			? related
			: null;
	}
}
