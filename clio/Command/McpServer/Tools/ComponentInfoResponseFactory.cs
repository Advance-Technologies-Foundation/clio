using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared factory for the composite-related <see cref="ComponentInfoResponse"/> shapes used by
/// BOTH the <c>get-component-info</c> MCP tool (<see cref="ComponentInfoTool"/>) and the CLI verb
/// (<see cref="ComponentInfoCommand"/>). These two surfaces are siblings in the command layer, not
/// a hierarchy — keeping the shared builders here (rather than as <c>internal static</c> members of
/// the MCP tool that the CLI reaches into) makes the dependency explicit and lets either surface
/// evolve without silently breaking the other. The per-surface error wording is still passed in by
/// each caller, so the surfaces stay free to phrase their own messages.
/// </summary>
public static class ComponentInfoResponseFactory {
	/// <summary>
	/// Cap on the "did you mean" shortlist a not-found response echoes, so it never returns the
	/// full ~199-item catalog as "suggestions".
	/// </summary>
	private const int MaxNotFoundSuggestions = 8;

	/// <summary>Case-insensitive composite lookup by caption. Shared by the MCP tool and the CLI verb.</summary>
	internal static CompositeDefinition? FindComposite(IReadOnlyList<CompositeDefinition>? composites, string caption) =>
		(composites ?? []).FirstOrDefault(item => string.Equals(item.Caption, caption, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Builds the not-found response for an unknown composite caption, listing the known
	/// captions so the caller can correct the lookup or fall back to list mode. Shared by
	/// the MCP tool and the CLI verb so both surfaces produce identical envelopes.
	/// </summary>
	internal static ComponentInfoResponse CreateCompositeNotFoundResponse(
		IReadOnlyList<CompositeDefinition>? composites,
		string caption,
		bool isMobile,
		string? resolvedTargetVersion,
		string? resolvedFrom,
		string? resolvedFromReason) {
		IReadOnlyList<CompositeDefinition> all = composites ?? [];
		string known;
		if (all.Count > 0) {
			known = "known composites: " + string.Join(", ", all.Select(item => $"'{item.Caption}'"));
		} else if (isMobile) {
			known = "composites are a web-only Designer feature; the mobile catalog has none — query the web component catalog instead";
		} else {
			known = "this catalog declares no composites";
		}
		return new ComponentInfoResponse {
			Success = false,
			Mode = "composite",
			Error = $"Composite '{caption}' was not found ({known}). "
				+ "Omit 'composite' and use list mode to see every composite with its description.",
			Count = 0,
			Items = [],
			ResolvedTargetVersion = resolvedTargetVersion,
			ResolvedFrom = resolvedFrom,
			ResolvedFromReason = resolvedFromReason
		};
	}

	/// <summary>
	/// Builds the list-shaped error response for the <c>composite</c> / <c>component-type</c>
	/// mutual-exclusivity guard, shared by the MCP tool and the CLI verb so both surfaces emit
	/// the same envelope (<c>mode:"list"</c>, empty items, version markers). The per-surface
	/// wording is passed in — the CLI prefixes <c>get-component-info:</c> and spells the flag
	/// <c>--composite</c>, the MCP tool quotes the JSON field names.
	/// </summary>
	internal static ComponentInfoResponse CreateMutualExclusivityError(
		string message,
		string? resolvedTargetVersion,
		string? resolvedFrom,
		string? resolvedFromReason) =>
		new() {
			Success = false,
			Mode = "list",
			Error = message,
			Count = 0,
			Items = [],
			ResolvedTargetVersion = resolvedTargetVersion,
			ResolvedFrom = resolvedFrom,
			ResolvedFromReason = resolvedFromReason
		};

	/// <summary>
	/// Builds the not-found response for an unknown <c>component-type</c> using name/description
	/// resolution: the agent typically reaches for a human label ("Expanded list") as if it were a
	/// component, so when the exact type id misses we (1) match the requested value against COMPONENTS
	/// by name/description/synonyms/use-cases, falling back to closest-by-distance suggestions for
	/// typo tolerance, and (2) match it against COMPOSITES by caption/description. When the value
	/// names a composite but no component matches it, the error ROUTES the agent to
	/// <c>composite="&lt;caption&gt;"</c> instead of leaving it to hand-build — this works for ANY
	/// composite, not a hard-coded set. Composites are still surfaced (search-filtered) on the
	/// non-routing path so the "composites exist" discovery hint is preserved. Shared by the MCP tool
	/// and the CLI verb so both surfaces resolve identically.
	/// </summary>
	internal static ComponentInfoResponse CreateComponentNotFoundResponse(
		IReadOnlyList<ComponentRegistryEntry> entries,
		IReadOnlyList<CompositeDefinition>? composites,
		string requestedType,
		string? search,
		string? resolvedTargetVersion,
		string? resolvedFrom,
		string? resolvedFromReason) {
		string query = requestedType.Trim();

		// Does the requested label name a composite? An EXACT caption match (case-insensitive) means the
		// caller reached for a composite by its human label and MUST be routed there regardless of any fuzzy
		// component match — otherwise a caption like "Attachments" or "Next steps" that substring-matches a
		// component's description would set hasComponentMatch and silently suppress the composite route. A
		// substring-only composite match (no exact caption) routes only when no component matched — the
		// weaker signal.
		IReadOnlyList<CompositeDefinition> queryComposites = ComponentInfoGrouping.FilterComposites(composites, query);
		CompositeDefinition? exactComposite = queryComposites
			.FirstOrDefault(item => string.Equals(item.Caption, query, StringComparison.OrdinalIgnoreCase));

		// "Ready component" pass — match the label against component name/description/synonyms/use-cases.
		IReadOnlyList<ComponentRegistryEntry> nameMatches = ComponentInfoGrouping.FilterEntries(entries, query);
		bool hasComponentMatch = nameMatches.Count > 0;

		bool routeToComposite = exactComposite is not null || (!hasComponentMatch && queryComposites.Count > 0);
		if (routeToComposite) {
			// Surface ONLY the matched composite(s); component suggestions here would be noise the directive
			// tells the agent to ignore. The directive caption prefers the exact match.
			string directiveCaption = (exactComposite ?? queryComposites[0]).Caption;
			string captions = string.Join(", ", queryComposites.Select(item => $"'{item.Caption}'"));
			return new ComponentInfoResponse {
				Success = false,
				Mode = "list",
				Error = $"'{query}' is not a component type — no such componentType exists in the catalog. "
					+ $"Searching composites found: {captions}. "
					+ $"REQUIRED: call get-component-info composite=\"{directiveCaption}\" to get the authoritative assembly recipe. "
					+ "Do NOT synthesize this structure from memory, guidance articles, or raw component docs — "
					+ "those sources are incomplete and will produce a broken result.",
				Count = 0,
				Items = [],
				Composites = ComponentInfoGrouping.CreateCompositeItems(queryComposites),
				ResolvedTargetVersion = resolvedTargetVersion,
				ResolvedFrom = resolvedFrom,
				ResolvedFromReason = resolvedFromReason
			};
		}

		// No composite route. Suggestions come from the name matches when present (the "ready component"
		// half of name->composite), else the closest-by-distance shortlist (typo tolerance). SuggestForUnknown
		// is computed lazily here so the routing branch above never pays for it.
		IReadOnlyList<ComponentRegistryEntry> suggestions = hasComponentMatch
			? nameMatches.Take(MaxNotFoundSuggestions).ToArray()
			: ComponentInfoGrouping.SuggestForUnknown(entries, query, search, MaxNotFoundSuggestions);

		// No component, no composite match. The message explains the two-step search so the agent
		// understands both paths were tried and neither matched.
		string error = hasComponentMatch
			? $"'{query}' is not a component type. "
				+ $"Showing {suggestions.Count} component(s) matching '{query}' by name/description — pass the correct componentType, "
				+ "or omit 'component-type' to list the full catalog (components AND composites)."
			: $"'{query}' is not a component type and does not match any composite. "
				+ $"Showing the {suggestions.Count} closest known type(s) — pass the correct componentType, "
				+ "or omit 'component-type' to list the full catalog (components AND composites).";

		// Keep the search-filtered composites section so the agent still sees that composites exist.
		IReadOnlyList<CompositeSummary> compositeItems = ComponentInfoGrouping.CreateCompositeItems(
			ComponentInfoGrouping.FilterComposites(composites, search));

		return new ComponentInfoResponse {
			Success = false,
			Mode = "list",
			Error = error,
			Count = suggestions.Count,
			Items = ComponentInfoGrouping.CreateItems(suggestions),
			Composites = compositeItems.Count == 0 ? null : compositeItems,
			ResolvedTargetVersion = resolvedTargetVersion,
			ResolvedFrom = resolvedFrom,
			ResolvedFromReason = resolvedFromReason
		};
	}

	/// <summary>
	/// Builds the <c>mode: "composite"</c> detail response. When the composite declares docs
	/// but none could be loaded (transient CDN/cache failure), <c>documentationUnavailable</c>
	/// is set so the agent can distinguish that from a composite that genuinely ships no docs —
	/// without it, both collapse to a content-less <c>success: true</c>. Shared by the MCP tool
	/// and the CLI verb.
	/// </summary>
	internal static ComponentInfoResponse CreateCompositeDetailResponse(
		CompositeDefinition composite,
		string? documentation,
		string? resolvedTargetVersion,
		string? resolvedFrom,
		string? resolvedFromReason) {
		bool declaresDocs = composite.Docs is { Count: > 0 };
		bool documentationMissing = string.IsNullOrEmpty(documentation);
		return new ComponentInfoResponse {
			Success = true,
			Mode = "composite",
			Count = 1,
			Caption = composite.Caption,
			Description = string.IsNullOrWhiteSpace(composite.Description) ? null : composite.Description,
			Documentation = documentationMissing ? null : documentation,
			DocumentationUnavailable = declaresDocs && documentationMissing ? true : null,
			ResolvedTargetVersion = resolvedTargetVersion,
			ResolvedFrom = resolvedFrom,
			ResolvedFromReason = resolvedFromReason
		};
	}
}
