namespace Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

// NAME ORACLE for embedded Freedom UI overrides in classic Mobile-wizard settings (ENG-95733).
//
// A classic settings schema can carry viewConfigDiff / viewModelConfigDiff / modelConfigDiff. Those operations
// address elements by the names the MOBILE RUNTIME generates when it converts the same wizard metadata on the
// device — a dialect that exists only at runtime. The Mobile Freedom UI designer works with the shipped
// template's names instead. Re-pointing an override therefore starts by answering one question: "what would
// the runtime have called this, and what did it MEAN?".
//
// This type answers exactly that and nothing more. It does not reproduce the runtime's JSON; it reproduces the
// runtime's NAME INVENTORY for one parsed source, and inverts a name back into the meaning behind it. The
// templates themselves are data (mobileLegacyRuntimeNames in the conversion rules), so a new runtime element is
// added to a table rather than to a branch.

/// <summary>
/// Roles the runtime-name table can assign. Only the roles the converter actually acts on are named here; a role
/// the data declares but this list does not know is carried through as an unrecognised anchor and REPORTED — never
/// guessed at.
/// </summary>
public static class LegacyRuntimeRoles {
	/// <summary>The screen root (<c>ViewConfig</c>) — carries title, actions, floatAction and header.</summary>
	public const string ScreenRoot = "screenRoot";

	/// <summary>The list component.</summary>
	public const string List = "list";

	/// <summary>The list row the wizard columns are rendered by.</summary>
	public const string ListRow = "listRow";

	/// <summary>A column from the wizard <c>subtitleItems</c> bucket, rendered in the row's <c>subtitles</c> slot.</summary>
	public const string SubtitleField = "subtitleField";

	/// <summary>A column from the wizard <c>groupItems</c> bucket, rendered in the row's <c>body</c> slot.</summary>
	public const string BodyField = "bodyField";

	/// <summary>The vestigial body-column marker the runtime removes when the last group column goes.</summary>
	public const string BodyColumnMarker = "bodyColumnMarker";

	/// <summary>The floating "add record" button.</summary>
	public const string AddButton = "addButton";

	/// <summary>One search column of the search filter (entity-schema derived, not enumerable from the settings).</summary>
	public const string SearchExpression = "searchExpression";

	/// <summary>One column offered in the sort tool's option list.</summary>
	public const string SortOption = "sortOption";
}

/// <summary>
/// One runtime-generated name together with the meaning behind it.
/// </summary>
/// <param name="RuntimeName">The name as the runtime would have generated it.</param>
/// <param name="Role">The role from the runtime-name table (see <see cref="LegacyRuntimeRoles"/>).</param>
/// <param name="Bucket">Wizard bucket the column came from, when the role is column-bound.</param>
/// <param name="Slot">Runtime slot the element sits in (<c>subtitles</c> / <c>body</c>), when it has one.</param>
/// <param name="ColumnPath">The wizard column path, when the role is column-bound.</param>
/// <param name="FromInventory">
/// True when the name was ENUMERATED from this source's own columns — the strongest possible evidence that the
/// override refers to something this conversion actually produced. False when the name only matched a template:
/// the meaning is known, but it points at something this source does not contain, which is a reportable fact
/// rather than a conversion input.
/// </param>
public sealed record LegacyRuntimeAnchor(
	string RuntimeName,
	string Role,
	string Bucket,
	string Slot,
	string ColumnPath,
	bool FromInventory);

/// <summary>
/// The runtime name inventory for one parsed source, plus reverse lookup for names outside it.
/// </summary>
public sealed class LegacyRuntimeNameInventory {

	private readonly Dictionary<string, LegacyRuntimeAnchor> _byName;
	private readonly IReadOnlyList<(Regex Pattern, MobileLegacyRuntimeAnchorRule Rule)> _patterns;

	internal LegacyRuntimeNameInventory(
		Dictionary<string, LegacyRuntimeAnchor> byName,
		IReadOnlyList<(Regex Pattern, MobileLegacyRuntimeAnchorRule Rule)> patterns) {
		_byName = byName;
		_patterns = patterns;
	}

	/// <summary>Every name the runtime would have generated for this source, in table order.</summary>
	public IReadOnlyCollection<string> Names => _byName.Keys;

	/// <summary>Whether the table carried any usable template at all (an empty table disables the override pass).</summary>
	public bool IsEmpty => _byName.Count == 0 && _patterns.Count == 0;

	/// <summary>
	/// Resolves a name an override operation addresses back into its meaning.
	/// </summary>
	/// <param name="runtimeName">The <c>name</c> an override operation targets.</param>
	/// <returns>The anchor, or null when no template in the table explains the name.</returns>
	public LegacyRuntimeAnchor Resolve(string runtimeName) {
		if (string.IsNullOrWhiteSpace(runtimeName)) {
			return null;
		}
		if (_byName.TryGetValue(runtimeName, out LegacyRuntimeAnchor known)) {
			return known;
		}
		foreach ((Regex pattern, MobileLegacyRuntimeAnchorRule rule) in _patterns) {
			Match match = pattern.Match(runtimeName);
			if (!match.Success) {
				continue;
			}
			Group column = match.Groups["column"];
			return new LegacyRuntimeAnchor(runtimeName, rule.Role, rule.Bucket, rule.Slot,
				column.Success ? column.Value : null, false);
		}
		return null;
	}
}

/// <summary>
/// Reproduces the element names the mobile runtime would have generated for a parsed classic list source, and
/// inverts them back into the meaning an embedded override was written against.
/// </summary>
public static class LegacyRuntimeNameOracle {

	private const string EntityPlaceholder = "{entity}";
	private const string ColumnPlaceholder = "{column}";
	private const string BucketItems = "items";
	private const string BucketSubtitleItems = "subtitleItems";
	private const string BucketGroupItems = "groupItems";

	/// <summary>An inventory built from no table at all — the override pass is then off and every section is reported.</summary>
	public static readonly LegacyRuntimeNameInventory Empty = new([], []);

	/// <summary>
	/// Builds the runtime name inventory for one parsed source.
	/// </summary>
	/// <param name="settings">The parsed classic list settings.</param>
	/// <param name="nameSet">The runtime-name table from the conversion rules; null or empty yields <see cref="Empty"/>.</param>
	/// <returns>The inventory; never null.</returns>
	/// <remarks>
	/// Templates carrying no placeholder are matched BEFORE templates carrying <c>{column}</c>, so the literal
	/// body-column marker is never read as a column that happens to be named "Column".
	/// </remarks>
	public static LegacyRuntimeNameInventory Build(LegacyGridPageSettings settings, MobileLegacyRuntimeNameSet nameSet) {
		ArgumentNullException.ThrowIfNull(settings);
		IReadOnlyList<MobileLegacyRuntimeAnchorRule> anchors = nameSet?.Anchors;
		if (anchors is null || anchors.Count == 0) {
			return Empty;
		}
		string entity = settings.EntitySchemaName ?? string.Empty;
		var byName = new Dictionary<string, LegacyRuntimeAnchor>(StringComparer.Ordinal);
		var literals = new List<(Regex, MobileLegacyRuntimeAnchorRule)>();
		var parameterised = new List<(Regex, MobileLegacyRuntimeAnchorRule)>();

		foreach (MobileLegacyRuntimeAnchorRule rule in anchors) {
			if (string.IsNullOrWhiteSpace(rule?.Pattern) || string.IsNullOrWhiteSpace(rule.Role)) {
				continue;
			}
			string resolved = rule.Pattern.Replace(EntityPlaceholder, entity, StringComparison.Ordinal);
			bool hasColumn = resolved.Contains(ColumnPlaceholder, StringComparison.Ordinal);
			(hasColumn ? parameterised : literals).Add((BuildPattern(resolved, hasColumn), rule));
			if (!hasColumn) {
				// A fixed element: it exists for every source, so it enumerates as itself.
				byName.TryAdd(resolved, new LegacyRuntimeAnchor(resolved, rule.Role, null, rule.Slot, null, true));
				continue;
			}
			foreach (LegacyGridColumn column in ColumnsOf(settings, rule.Bucket)) {
				string name = resolved.Replace(ColumnPlaceholder, column.ColumnName, StringComparison.Ordinal);
				byName.TryAdd(name,
					new LegacyRuntimeAnchor(name, rule.Role, rule.Bucket, rule.Slot, column.ColumnName, true));
			}
		}
		// Literals first so an exact marker always wins over a column template that could also match it.
		return new LegacyRuntimeNameInventory(byName, [.. literals, .. parameterised]);
	}

	/// <summary>Columns of the named wizard bucket; empty when the rule names no bucket or an unknown one.</summary>
	private static IReadOnlyList<LegacyGridColumn> ColumnsOf(LegacyGridPageSettings settings, string bucket) => bucket switch {
		BucketItems => settings.Items,
		BucketSubtitleItems => settings.SubtitleItems,
		BucketGroupItems => settings.GroupItems,
		_ => []
	};

	/// <summary>
	/// Compiles one resolved template into an anchored matcher. Everything around <c>{column}</c> is literal, so
	/// a name is only ever read as a column when the rest of it matches the template exactly.
	/// </summary>
	private static Regex BuildPattern(string resolved, bool hasColumn) {
		string expression = hasColumn
			? string.Join("(?<column>.+)",
				resolved.Split(ColumnPlaceholder, StringSplitOptions.None).Select(Regex.Escape))
			: Regex.Escape(resolved);
		return new Regex($"^{expression}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
	}
}
