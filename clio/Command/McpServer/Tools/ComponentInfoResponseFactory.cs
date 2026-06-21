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
