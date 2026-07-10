using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.McpServer;

/// <summary>
/// Classifies why a legacy/alternate name maps to a canonical MCP tool name.
/// </summary>
public enum McpToolCompatibilityKind {
	/// <summary>A deprecated/legacy alias of a still-current tool (the canonical is the replacement).</summary>
	DeprecatedAlias,

	/// <summary>The named tool has been removed; the entry exists only to explain the removal / point at a replacement.</summary>
	Removed
}

/// <summary>
/// Identifies which surface owns the canonical name of a compatibility entry.
/// </summary>
public enum McpToolSurfaceOwner {
	/// <summary>A clio MCP tool (invokable by clio).</summary>
	Clio,

	/// <summary>A command owned by another tool (e.g. a foreign CLI); recognised for diagnostics but not invokable by clio.</summary>
	Foreign
}

/// <summary>
/// A single durable mapping from one or more legacy/alias names to a canonical MCP tool name.
/// This is the MCP-tool analogue of clio's CLI-flag backward-compatibility policy (kebab-case names
/// plus hidden aliases): it lets guidance written against an older tool name keep resolving after the
/// tool surface evolves.
/// </summary>
/// <param name="CanonicalName">The current, canonical MCP tool name that aliases resolve to.</param>
/// <param name="Aliases">The legacy/alternate names that resolve to <paramref name="CanonicalName"/>.</param>
/// <param name="Kind">Why the mapping exists (deprecated alias vs removed tool).</param>
/// <param name="DeprecatedSince">The clio version the alias was deprecated in, when known; otherwise <c>null</c>.</param>
/// <param name="Replacement">An explicit replacement tool name for a <see cref="McpToolCompatibilityKind.Removed"/> entry, when the replacement differs from the canonical; otherwise <c>null</c>.</param>
/// <param name="Owner">Which surface owns the canonical name.</param>
public sealed record McpToolCompatibilityEntry(
	string CanonicalName,
	IReadOnlyList<string> Aliases,
	McpToolCompatibilityKind Kind,
	string DeprecatedSince,
	string Replacement,
	McpToolSurfaceOwner Owner);

/// <summary>
/// The single source of truth mapping legacy / renamed / deprecated MCP tool names to their canonical
/// name. Consumed by the forgiving call-tool handler, the <c>clio-run</c> executor, the
/// <c>get-tool-contract</c> index, and the workspace-guidance drift test, so all four agree on how an
/// evolved tool name resolves. Name comparisons are case-insensitive; the canonical name is always
/// emitted in its declared casing.
/// </summary>
public interface IMcpToolCompatibilityCatalog {
	/// <summary>
	/// Attempts to resolve a requested name to its canonical MCP tool name via a declared alias.
	/// </summary>
	/// <param name="requestedName">The name a caller supplied (may be a legacy alias).</param>
	/// <param name="canonicalName">The canonical tool name when <paramref name="requestedName"/> is a declared alias; otherwise <c>null</c>.</param>
	/// <param name="entry">The matched compatibility entry when the lookup hits; otherwise <c>null</c>.</param>
	/// <returns><c>true</c> when <paramref name="requestedName"/> is a declared alias; otherwise <c>false</c> (no throw on miss).</returns>
	bool TryResolveAlias(string requestedName, out string canonicalName, out McpToolCompatibilityEntry entry);

	/// <summary>All compatibility entries in the catalog, in declaration order.</summary>
	IReadOnlyCollection<McpToolCompatibilityEntry> Entries { get; }
}

/// <summary>
/// Default <see cref="IMcpToolCompatibilityCatalog"/> built from a static seed of known compatibility
/// entries. The constructor validates the whole catalog for internal consistency and throws on any
/// collision, so a malformed catalog fails fast at DI build time (the container is built with
/// <c>ValidateOnBuild = true</c>) rather than on first resolution.
/// </summary>
public sealed class McpToolCompatibilityCatalog : IMcpToolCompatibilityCatalog {

	// The durable compatibility seed. Add an entry here when an MCP tool is renamed or removed, instead
	// of registering a duplicate [McpServerTool] method for the old name.
	private static readonly IReadOnlyList<McpToolCompatibilityEntry> SeedEntries = new[] {
		new McpToolCompatibilityEntry(
			CanonicalName: "restart-by-environment-name",
			Aliases: new[] { "restart-by-environmentName" },
			Kind: McpToolCompatibilityKind.DeprecatedAlias,
			DeprecatedSince: null,
			Replacement: null,
			Owner: McpToolSurfaceOwner.Clio)
	};

	private readonly IReadOnlyList<McpToolCompatibilityEntry> _entries;
	private readonly IReadOnlyDictionary<string, (string Canonical, McpToolCompatibilityEntry Entry)> _aliasIndex;

	/// <summary>
	/// Builds the catalog from the built-in seed. Used by DI.
	/// </summary>
	public McpToolCompatibilityCatalog()
		: this(SeedEntries) {
	}

	/// <summary>
	/// Builds the catalog from an explicit entry list. Exposed for tests so a synthetic (including a
	/// deliberately colliding) catalog can be validated without editing the production seed.
	/// </summary>
	/// <param name="entries">The compatibility entries to index and validate.</param>
	/// <exception cref="ArgumentNullException">When <paramref name="entries"/> is <c>null</c>.</exception>
	/// <exception cref="InvalidOperationException">When the catalog contains a collision (duplicate canonical, duplicate alias, an alias equal to a canonical, or an empty name).</exception>
	internal McpToolCompatibilityCatalog(IReadOnlyList<McpToolCompatibilityEntry> entries) {
		ArgumentNullException.ThrowIfNull(entries);
		_entries = entries;
		_aliasIndex = BuildValidatedAliasIndex(entries);
	}

	/// <inheritdoc />
	public IReadOnlyCollection<McpToolCompatibilityEntry> Entries => _entries;

	/// <inheritdoc />
	public bool TryResolveAlias(string requestedName, out string canonicalName, out McpToolCompatibilityEntry entry) {
		if (!string.IsNullOrWhiteSpace(requestedName)
			&& _aliasIndex.TryGetValue(requestedName.Trim(), out (string Canonical, McpToolCompatibilityEntry Entry) hit)) {
			canonicalName = hit.Canonical;
			entry = hit.Entry;
			return true;
		}
		canonicalName = null;
		entry = null;
		return false;
	}

	// Validates internal consistency and returns the alias -> (canonical, entry) lookup. Fails closed on
	// ANY collision so a malformed catalog cannot boot: a name that is both a canonical and an alias, the
	// same alias declared twice, a duplicate canonical, or an empty name would each make resolution
	// ambiguous. Cross-checking that each canonical is a real registered tool is enforced separately by a
	// unit test (kept out of the constructor to avoid coupling the catalog to the heavy tool registry and
	// to feature-gating).
	private static IReadOnlyDictionary<string, (string, McpToolCompatibilityEntry)> BuildValidatedAliasIndex(
		IReadOnlyList<McpToolCompatibilityEntry> entries) {
		HashSet<string> canonicals = new(StringComparer.OrdinalIgnoreCase);
		foreach (McpToolCompatibilityEntry entry in entries) {
			if (entry is null) {
				throw new InvalidOperationException("Compatibility catalog contains a null entry.");
			}
			if (string.IsNullOrWhiteSpace(entry.CanonicalName)) {
				throw new InvalidOperationException("Compatibility catalog contains an entry with an empty canonical name.");
			}
			if (!canonicals.Add(entry.CanonicalName.Trim())) {
				throw new InvalidOperationException(
					$"Compatibility catalog declares canonical name '{entry.CanonicalName}' more than once.");
			}
		}

		Dictionary<string, (string, McpToolCompatibilityEntry)> aliasIndex =
			new(StringComparer.OrdinalIgnoreCase);
		foreach (McpToolCompatibilityEntry entry in entries) {
			foreach (string alias in entry.Aliases ?? Array.Empty<string>()) {
				if (string.IsNullOrWhiteSpace(alias)) {
					throw new InvalidOperationException(
						$"Compatibility catalog entry for canonical '{entry.CanonicalName}' declares an empty alias.");
				}
				string trimmedAlias = alias.Trim();
				if (canonicals.Contains(trimmedAlias)) {
					throw new InvalidOperationException(
						$"Compatibility catalog alias '{alias}' collides with a canonical tool name.");
				}
				if (!aliasIndex.TryAdd(trimmedAlias, (entry.CanonicalName.Trim(), entry))) {
					throw new InvalidOperationException(
						$"Compatibility catalog declares alias '{alias}' more than once.");
				}
			}
		}
		return aliasIndex;
	}
}
