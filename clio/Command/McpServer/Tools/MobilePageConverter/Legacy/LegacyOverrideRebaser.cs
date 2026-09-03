namespace Clio.Command.McpServer.Tools.MobilePageConverter.Legacy;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;

// REBASE of embedded Freedom UI overrides onto the converted page (ENG-95733).
//
// An override in a classic settings schema addresses the names the mobile RUNTIME generates. This step turns each
// operation into one of exactly three outcomes, and never anything in between:
//
//   source-edit   the operation says something the wizard model itself can say (drop a column), so the SOURCE is
//                 edited and the existing pure analyzer re-derives the page from it. One emit path, so determinism
//                 and the golden fixture survive, and bindings are re-derived instead of copied.
//   target-delta  the operation says something only the target dialect can say (remove the floating action, set a
//                 cache rule), so it becomes an extra designer-dialect operation.
//   reported      everything else. Never guessed at, never half-applied.
//
// The all-or-nothing rule is structural, not a discipline: operations are grouped by SUBJECT (the column they
// touch, or the element they target) and a group resolves entirely or not at all. This is what stops the shipped
// "move Account from body to subtitles" pair from degrading into "delete Account", which is worse than doing
// nothing.

/// <summary>The outcome kinds an override operation can resolve into.</summary>
public static class LegacyOverrideLanes {
	/// <summary>Expressed as an edit to the wizard source; the analyzer re-derives the page from it.</summary>
	public const string SourceEdit = "source-edit";

	/// <summary>Expressed as an extra designer-dialect operation on the converted page.</summary>
	public const string TargetDelta = "target-delta";

	/// <summary>Not carried over; reported with a reason.</summary>
	public const string Reported = "reported";
}

/// <summary>What happened to one override operation.</summary>
/// <param name="Section">The settings key the operation came from.</param>
/// <param name="Index">Its position in that section, so the user can find it in the source.</param>
/// <param name="Operation">The diff operation (<c>merge</c> / <c>remove</c> / <c>insert</c> / …).</param>
/// <param name="Target">The runtime name the operation addressed.</param>
/// <param name="Lane">One of <see cref="LegacyOverrideLanes"/>.</param>
/// <param name="Effect">What it did on the converted page; null when it was only reported.</param>
/// <param name="Reason">Why it could not be carried; null when it was.</param>
public sealed record LegacyOverrideOutcome(
	string Section,
	int Index,
	string Operation,
	string Target,
	string Lane,
	string Effect,
	string Reason);

/// <summary>The result of rebasing every embedded override of one source.</summary>
/// <param name="Settings">The wizard settings after the source-edit lane; the analyzer converts THESE.</param>
/// <param name="ViewConfigOperations">Extra designer-dialect view operations to append to the page body.</param>
/// <param name="ViewModelConfigOperations">Extra path-addressed view-model operations.</param>
/// <param name="ModelConfigOperations">Extra path-addressed model operations.</param>
/// <param name="ElementValueOverrides">
/// Designer element name → property values an override sets on it. The override WINS on every key it names: the
/// converter folds these over its own values for an element it writes itself, and emits them as an extra merge
/// for a template element it does not otherwise touch.
/// </param>
/// <param name="RequiredColumns">
/// Column paths an override's bindings reference that the wizard buckets never declared. They are declared in
/// both data sections exactly like a wizard column, otherwise the carried binding would be dead.
/// </param>
/// <param name="Warnings">
/// Plain-language warnings about override operations whose outcome on the converted page differs from what the
/// override asked for, or that were skipped. They are surfaced through the guide's <c>constraints</c>, which the
/// caller cannot skip.
/// </param>
/// <param name="Outcomes">One entry per override operation, in source order.</param>
public sealed record LegacyOverrideRebaseResult(
	LegacyGridPageSettings Settings,
	IReadOnlyList<JsonObject> ViewConfigOperations,
	IReadOnlyList<JsonObject> ViewModelConfigOperations,
	IReadOnlyList<JsonObject> ModelConfigOperations,
	IReadOnlyDictionary<string, JsonObject> ElementValueOverrides,
	IReadOnlyList<string> RequiredColumns,
	IReadOnlyList<string> Warnings,
	IReadOnlyList<LegacyOverrideOutcome> Outcomes);

/// <summary>
/// Re-points embedded Freedom UI override operations from the mobile runtime's generated names onto the converted
/// page, using the runtime-name table (<see cref="LegacyRuntimeNameOracle"/>) and the target template's own
/// element names.
/// </summary>
public static class LegacyOverrideRebaser {

	private const string ViewConfigSection = "viewConfigDiff";
	private const string ModelConfigSection = "modelConfigDiff";
	private const string ItemsPlaceholder = "{items}";
	private const string PrimaryDataSourcePlaceholder = "{pds}";
	private const string PrimaryDataSourceAlias = "PDS";
	private const string BindingPrefix = "$";
	private const string AttributePrefix = "PDS_";

	/// <summary>Operation kinds, in the order the mobile runtime's applier processes them.</summary>
	private static readonly string[] RuntimeOperationOrder = ["merge", "remove", "move", "insert", "set"];

	/// <summary>
	/// Rebases every supported override section of one source.
	/// </summary>
	/// <param name="settings">The parsed wizard settings.</param>
	/// <param name="sections">The classified override sections; only supported ones carrying operations are used.</param>
	/// <param name="inventory">The runtime name inventory for this source.</param>
	/// <param name="template">The target template's element names.</param>
	/// <param name="nameSet">The runtime-name table, which also carries the designer targets.</param>
	/// <returns>The rebase result; never null. With no usable table nothing is carried and everything is reported.</returns>
	public static LegacyOverrideRebaseResult Rebase(
		LegacyGridPageSettings settings,
		IReadOnlyList<LegacyOverrideSection> sections,
		LegacyRuntimeNameInventory inventory,
		MobileLegacyTemplateRule template,
		MobileLegacyRuntimeNameSet nameSet,
		IReadOnlyDictionary<string, string> columnCaptions = null) {
		ArgumentNullException.ThrowIfNull(settings);
		template ??= LegacyMobileListAnalysisService.DefaultGridPageTemplate;
		var outcomes = new List<LegacyOverrideOutcome>();
		var viewOps = new List<JsonObject>();
		var viewModelOps = new List<JsonObject>();
		var modelOps = new List<JsonObject>();
		var elementValues = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
		var requiredColumns = new List<string>();
		var warnings = new List<string>();
		LegacyGridPageSettings current = settings;

		IReadOnlyList<LegacyOverrideSection> usable = (sections ?? [])
			.Where(s => s is { Supported: true, Operations: not null })
			.ToList();
		if (usable.Count == 0) {
			return new LegacyOverrideRebaseResult(current, viewOps, viewModelOps, modelOps, elementValues,
				requiredColumns, warnings, outcomes);
		}
		Dictionary<string, MobileLegacyRuntimeAnchorRule> byRole = BuildRoleIndex(nameSet);

		foreach (LegacyOverrideSection section in usable) {
			List<Operation> operations = Read(section);
			if (string.Equals(section.Section, ViewConfigSection, StringComparison.Ordinal)) {
				current = RebaseViewConfig(current, operations, inventory, byRole, viewOps, elementValues,
					requiredColumns, warnings, outcomes, columnCaptions);
				continue;
			}
			List<JsonObject> target = string.Equals(section.Section, ModelConfigSection, StringComparison.Ordinal)
				? modelOps
				: viewModelOps;
			RebasePathSection(operations, inventory, byRole, template, target, warnings, outcomes);
		}
		return new LegacyOverrideRebaseResult(current, viewOps, viewModelOps, modelOps, elementValues,
			requiredColumns, warnings, outcomes);
	}

	/// <summary>
	/// The view lane. Operations are grouped by the column they touch (or by the element they address), and a group
	/// is carried across only when EVERY operation in it resolves — a half-applied group is worse than none.
	/// </summary>
	private static LegacyGridPageSettings RebaseViewConfig(
		LegacyGridPageSettings settings,
		List<Operation> operations,
		LegacyRuntimeNameInventory inventory,
		Dictionary<string, MobileLegacyRuntimeAnchorRule> byRole,
		List<JsonObject> viewOps,
		Dictionary<string, JsonObject> elementValues,
		List<string> requiredColumns,
		List<string> warnings,
		List<LegacyOverrideOutcome> outcomes,
		IReadOnlyDictionary<string, string> columnCaptions) {
		var resolutions = new List<(Operation Op, LegacyRuntimeAnchor Anchor, Resolution Resolution)>();
		foreach (Operation op in operations) {
			LegacyRuntimeAnchor anchor = inventory?.Resolve(op.Name);
			resolutions.Add((op, anchor, ResolveView(op, anchor, byRole, settings, columnCaptions)));
		}

		// Subject = the column an operation touches, else the runtime element it addresses. A move arrives as a
		// remove plus an insert of the SAME column, so grouping by column is what keeps the pair together.
		foreach (IGrouping<string, (Operation Op, LegacyRuntimeAnchor Anchor, Resolution Resolution)> group in
			resolutions.GroupBy(r => r.Anchor?.ColumnPath ?? r.Op.Name ?? $"#{r.Op.Index}", StringComparer.Ordinal)) {
			List<(Operation Op, LegacyRuntimeAnchor Anchor, Resolution Resolution)> members = [.. group];

			// An attempted MOVE of one column between the runtime's two row slots. A converted row has a single
			// body slot and already shows the column there, so the pair is a no-op — and the removal must NOT be
			// applied on its own, or "move" would silently become "delete".
			if (IsSlotMove(members)) {
				foreach ((Operation op, LegacyRuntimeAnchor _, Resolution _) in members) {
					outcomes.Add(new LegacyOverrideOutcome(ViewConfigSection, op.Index, op.Kind, op.Name,
						LegacyOverrideLanes.Reported, null,
						$"Part of a move of column '{group.Key}' between the row's subtitle and body slots; a converted row has one slot, so nothing needed to change."));
				}
				warnings.Add($"Embedded override ({ViewConfigSection}[{string.Join(", ", members.Select(m => m.Op.Index))}]) moves column '{group.Key}' between the list row's subtitle and body slots. A converted list row has ONE slot and already shows the column there, so nothing was changed and the column is intact.");
				continue;
			}

			(Operation Op, LegacyRuntimeAnchor Anchor, Resolution Resolution) blocked =
				members.FirstOrDefault(m => !m.Resolution.Resolved);
			if (blocked.Op is not null) {
				string shared = members.Count > 1
					? $" It is one of {members.Count} operations on '{group.Key}', so none of them was applied — half of a move would delete the column instead of moving it."
					: string.Empty;
				foreach ((Operation op, LegacyRuntimeAnchor _, Resolution resolution) in members) {
					string reason = (op.Index == blocked.Op.Index ? resolution.Reason : blocked.Resolution.Reason) + shared;
					outcomes.Add(Report(ViewConfigSection, op, reason));
					warnings.Add($"Embedded override ({ViewConfigSection}[{op.Index}] {op.Kind} '{op.Name}') was NOT carried over. {reason}");
				}
				continue;
			}
			foreach ((Operation op, LegacyRuntimeAnchor anchor, Resolution resolution) in
				members.OrderBy(m => OperationRank(m.Op.Kind))) {
				if (resolution.DropColumnFrom is not null) {
					settings = DropColumn(settings, resolution.DropColumnFrom, anchor.ColumnPath);
				}
				if (resolution.AddColumnTo is not null) {
					settings = AddColumn(settings, resolution.AddColumnTo, resolution.AddedColumn);
				}
				viewOps.AddRange(resolution.TargetOperations);
				if (resolution.ElementName is not null) {
					Fold(elementValues, resolution.ElementName, resolution.ElementValues, resolution.AppendArrays);
				}
				foreach (string column in resolution.RequiredColumns) {
					if (!requiredColumns.Contains(column, StringComparer.Ordinal)) {
						requiredColumns.Add(column);
					}
				}
				if (resolution.Warning is not null) {
					warnings.Add(resolution.Warning);
				}
				bool editedSource = resolution.DropColumnFrom is not null || resolution.AddColumnTo is not null;
				bool touchedTarget = resolution.TargetOperations.Count > 0 || resolution.ElementName is not null;
				outcomes.Add(new LegacyOverrideOutcome(ViewConfigSection, op.Index, op.Kind, op.Name,
					editedSource ? LegacyOverrideLanes.SourceEdit
						: touchedTarget ? LegacyOverrideLanes.TargetDelta : LegacyOverrideLanes.Reported,
					resolution.Effect, null));
			}
		}
		return settings;
	}

	/// <summary>
	/// The data lanes. The target dialect addresses the view-model and model sections by PATH, so an operation is
	/// re-pointed when the element it names carries a designer path. Both <c>merge</c> and <c>insert</c> qualify:
	/// the runtime's <c>insert X into parent.property</c> and its <c>merge X</c> both mean "this is what X holds",
	/// and X's own designer path already encodes where that is.
	/// </summary>
	private static void RebasePathSection(
		List<Operation> operations,
		LegacyRuntimeNameInventory inventory,
		Dictionary<string, MobileLegacyRuntimeAnchorRule> byRole,
		MobileLegacyTemplateRule template,
		List<JsonObject> target,
		List<string> warnings,
		List<LegacyOverrideOutcome> outcomes) {
		foreach (Operation op in operations.OrderBy(o => OperationRank(o.Kind))) {
			LegacyRuntimeAnchor anchor = inventory?.Resolve(op.Name);
			MobileLegacyRuntimeAnchorRule rule = anchor is null ? null : Rule(byRole, anchor.Role);
			if (anchor is null || rule?.DesignerPath is null) {
				string reason = anchor is null
					? $"No runtime element named '{op.Name}' is generated for this source, so there is nothing to re-point it onto. It most likely came from another schema in the hierarchy or was hand-written."
					: $"The runtime element '{op.Name}' has no addressable counterpart in the converted page's data sections.";
				outcomes.Add(Report(op.Section, op, reason));
				warnings.Add($"Embedded override ({op.Section}[{op.Index}] {op.Kind} '{op.Name}') was SKIPPED. {reason}");
				continue;
			}
			bool carries = string.Equals(op.Kind, "merge", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(op.Kind, "insert", StringComparison.OrdinalIgnoreCase);
			if (!carries || op.Values is null) {
				string reason = $"Only an operation that SETS content ('merge' or 'insert') can be re-pointed onto a data-section path; '{op.Kind}' addresses runtime elements that the converted page does not declare.";
				outcomes.Add(Report(op.Section, op, reason));
				warnings.Add($"Embedded override ({op.Section}[{op.Index}] {op.Kind} '{op.Name}') was SKIPPED. {reason}");
				continue;
			}
			string[] segments = [.. rule.DesignerPath.Select(segment => ResolveSegment(segment, template))];
			target.Add(new JsonObject {
				["operation"] = "merge",
				["path"] = new JsonArray([.. segments.Select(segment => (JsonNode)JsonValue.Create(segment))]),
				["values"] = ToJsonNode(op.Values)
			});
			outcomes.Add(new LegacyOverrideOutcome(op.Section, op.Index, op.Kind, op.Name,
				LegacyOverrideLanes.TargetDelta,
				$"Re-pointed onto the converted page's {string.Join("/", segments)} path.", null));
		}
	}

	/// <summary>Decides what one view operation becomes, without applying anything.</summary>
	private static Resolution ResolveView(
		Operation op, LegacyRuntimeAnchor anchor, Dictionary<string, MobileLegacyRuntimeAnchorRule> byRole,
		LegacyGridPageSettings settings, IReadOnlyDictionary<string, string> columnCaptions) {
		if (anchor is null) {
			return Resolution.No(
				$"No runtime element named '{op.Name}' is generated for this source, so there is nothing to re-point it onto. It most likely came from another schema in the hierarchy or was hand-written.");
		}
		MobileLegacyRuntimeAnchorRule rule = Rule(byRole, anchor.Role);
		bool isRemove = string.Equals(op.Kind, "remove", StringComparison.OrdinalIgnoreCase);
		bool isMerge = string.Equals(op.Kind, "merge", StringComparison.OrdinalIgnoreCase);

		// A property removal on an element the designer models as a separate element (the floating action).
		if (isRemove && op.Properties.Count > 0) {
			IReadOnlyDictionary<string, string> targets = rule?.PropertyTargets;
			List<string> unmapped = [.. op.Properties.Where(p => targets is null || !targets.ContainsKey(p))];
			return unmapped.Count > 0
				? Resolution.No($"Property '{unmapped[0]}' of the runtime element '{op.Name}' has no counterpart on the converted page.")
				: Resolution.Target(
					[.. op.Properties.Select(p => Remove(targets[p]))],
					$"Removed the template element(s) {string.Join(", ", op.Properties.Select(p => $"'{targets[p]}'"))}, which is how the converted page carries what the runtime kept as a property of '{op.Name}'.");
		}
		if (isRemove && anchor.ColumnPath is not null) {
			// A column the wizard itself placed: say it in the wizard's own language and let the analyzer re-derive.
			return anchor.FromInventory
				? Resolution.Source(anchor.Bucket, $"Dropped column '{anchor.ColumnPath}' from the wizard '{anchor.Bucket}' bucket.")
				: Resolution.No($"The override removes column '{anchor.ColumnPath}', which this source's '{anchor.Bucket}' bucket does not contain.");
		}
		if (isRemove && rule?.DesignerName is not null) {
			return Resolution.Target([Remove(rule.DesignerName)], $"Removed the template element '{rule.DesignerName}'.");
		}
		if (isRemove) {
			return Resolution.No($"The runtime element '{op.Name}' has no counterpart on the converted page, so removing it cannot be expressed.");
		}
		if (isMerge && anchor.ColumnPath is not null) {
			// A per-column row property. The converted row carries only the column's value binding, so the label
			// switch — the one shape that occurs in shipped packages — cannot be honoured.
			return Resolution.No(op.Values?["label"] is not null
				? $"The override hides the label of column '{anchor.ColumnPath}'. The Mobile Freedom UI designer does not support controlling a list-row label yet — every body column shows its label — so the label could NOT be hidden and it stays visible on the converted page."
				: $"The override sets properties on the runtime's rendering of column '{anchor.ColumnPath}'; the converted row carries only that column's value binding, so they have no counterpart.");
		}
		if (isMerge && rule?.DesignerName is not null && op.Values is not null) {
			// The override WINS on every key it names. On the list row a binding addresses a column of the record,
			// so it is re-derived into the converted page's own attribute convention and the attribute is declared;
			// anywhere else the values are carried through untouched.
			bool isRow = string.Equals(anchor.Role, LegacyRuntimeRoles.ListRow, StringComparison.Ordinal);
			var columns = new List<string>();
			JsonNode values = ToJsonNode(op.Values);
			if (isRow) {
				values = RewriteBindings(values, columns);
			}
			JsonObject folded = values.AsObject();
			string extra = columns.Count > 0
				? $" Its binding(s) reference {string.Join(", ", columns.Select(c => $"'{c}'"))}, which the wizard buckets did not declare, so the attribute(s) were added to both data sections."
				: string.Empty;
			return Resolution.Element(rule.DesignerName, folded, columns,
				$"Set {string.Join(", ", folded.Select(pair => $"'{pair.Key}'"))} on the template element '{rule.DesignerName}'; the override wins over the converted value.{extra}");
		}
		if (string.Equals(anchor.Role, LegacyRuntimeRoles.SortOption, StringComparison.Ordinal)
			&& anchor.ColumnPath is not null && rule?.DesignerName is not null) {
			// One column offered in the sort tool. The runtime inserts it into the sort item's sortOptions as
			// { property }; the designer carries the same thing as an entry of SortButton.sortItems, shaped
			// { attributeName, caption } with the RAW column name. Several such inserts accumulate into one merge.
			LegacyGridColumn onPage = Column(settings, anchor.ColumnPath);
			string caption = onPage?.Caption;
			if (string.IsNullOrWhiteSpace(caption)) {
				columnCaptions?.TryGetValue(anchor.ColumnPath, out caption);
			}
			var entry = new JsonObject { ["attributeName"] = anchor.ColumnPath };
			if (!string.IsNullOrWhiteSpace(caption)) {
				// A caption is a display label. Falling back to the column name would put a machine name in front
				// of the user, so an unresolved caption is left out and reported instead of being invented.
				entry["caption"] = caption;
			}
			return Resolution.ElementEntry(rule.DesignerName, new JsonObject { ["sortItems"] = new JsonArray(entry) },
				[anchor.ColumnPath],
				$"Offered column '{anchor.ColumnPath}' in the sort tool of '{rule.DesignerName}'.",
				string.IsNullOrWhiteSpace(caption)
					? $"Embedded override ({op.Section}[{op.Index}] {op.Kind} '{op.Name}') offers column '{anchor.ColumnPath}' in the sort tool of '{rule.DesignerName}', but no caption could be resolved for it — the column is neither one of the page's own columns nor readable from the object. The sort option is emitted WITHOUT a caption: give it one before the page is written."
					: null);
		}
		if (anchor.ColumnPath is not null) {
			// The override adds a column to one of the runtime's row slots. A converted row has a single body slot,
			// so the column is added to the wizard model as an ordinary column — the value is shown, the separate
			// slot is not reproduced — and only when the page does not already carry it.
			if (Carries(settings, anchor.ColumnPath)) {
				return Resolution.Unchanged(
					$"Column '{anchor.ColumnPath}' is already shown in the list row, so nothing was changed.",
					$"Embedded override ({op.Section}[{op.Index}] {op.Kind} '{op.Name}') places column '{anchor.ColumnPath}' in the list row's '{anchor.Slot}' slot. The converted row already shows that column in its single body slot, so nothing was changed.");
			}
			var added = new LegacyGridColumn(op.Name, anchor.ColumnPath, null, op.Index, null,
				new Dictionary<string, JToken>());
			return Resolution.Add(LegacyGridPageSettingsParser.SubtitleItemsBucket, added,
				$"Added column '{anchor.ColumnPath}' to the list row.",
				$"Embedded override ({op.Section}[{op.Index}] {op.Kind} '{op.Name}') places column '{anchor.ColumnPath}' in the list row's '{anchor.Slot}' slot. A converted list row has ONE slot, so the column was added to the row body instead: its value is shown, but the separate '{anchor.Slot}' placement is not reproduced.");
		}
		return Resolution.No(
			$"Only a removal or a property merge can be re-pointed onto the converted page's view; '{op.Kind}' on '{op.Name}' carries runtime-dialect content that the target template does not declare.");
	}

	/// <summary>The wizard column with that path, from any bucket; null when the page does not carry it.</summary>
	private static LegacyGridColumn Column(LegacyGridPageSettings settings, string columnPath) =>
		settings.Items.Concat(settings.SubtitleItems).Concat(settings.GroupItems)
			.FirstOrDefault(c => string.Equals(c.ColumnName, columnPath, StringComparison.Ordinal));

	/// <summary>Whether any wizard bucket already carries the column, in which case an add would duplicate it.</summary>
	private static bool Carries(LegacyGridPageSettings settings, string columnPath) =>
		Column(settings, columnPath) is not null;

	/// <summary>
	/// Whether a group is an attempted move of ONE column between the runtime's row slots — a removal from one slot
	/// paired with an insertion into the other. Both collapse to the converted row's single body slot.
	/// </summary>
	private static bool IsSlotMove(
		List<(Operation Op, LegacyRuntimeAnchor Anchor, Resolution Resolution)> members) =>
		members.Count > 1
		&& members.All(m => m.Anchor?.ColumnPath is not null)
		&& members.Any(m => string.Equals(m.Op.Kind, "remove", StringComparison.OrdinalIgnoreCase))
		&& members.Any(m => string.Equals(m.Op.Kind, "insert", StringComparison.OrdinalIgnoreCase));

	/// <summary>Appends a column the override asked for to a wizard bucket, keeping the bucket ordered by row.</summary>
	private static LegacyGridPageSettings AddColumn(
		LegacyGridPageSettings settings, string bucket, LegacyGridColumn column) =>
		bucket switch {
			LegacyGridPageSettingsParser.ItemsBucket => settings with { Items = [.. settings.Items, column] },
			LegacyGridPageSettingsParser.SubtitleItemsBucket => settings with { SubtitleItems = [.. settings.SubtitleItems, column] },
			LegacyGridPageSettingsParser.GroupItemsBucket => settings with { GroupItems = [.. settings.GroupItems, column] },
			_ => settings
		};

	/// <summary>
	/// Rewrites row-level bindings into the converted page's attribute convention and records the columns they
	/// reference. A hand-written override commonly writes <c>$Photo</c> where the converted page declares
	/// <c>$PDS_Photo</c>; carrying it verbatim would leave a binding that resolves to nothing.
	/// </summary>
	private static JsonNode RewriteBindings(JsonNode node, List<string> columns) {
		switch (node) {
			case JsonObject source: {
				var rewritten = new JsonObject();
				foreach (KeyValuePair<string, JsonNode> pair in source) {
					rewritten[pair.Key] = RewriteBindings(pair.Value, columns);
				}
				return rewritten;
			}
			case JsonArray source:
				return new JsonArray([.. source.Select(item => RewriteBindings(item, columns))]);
			case JsonValue value when value.TryGetValue(out string text)
				&& text.StartsWith(BindingPrefix, StringComparison.Ordinal): {
				string name = text[BindingPrefix.Length..];
				if (name.Length == 0 || name.StartsWith(AttributePrefix, StringComparison.Ordinal)) {
					// Already in the converted page's convention; the source column cannot be recovered from an
					// underscored name, so the binding is left exactly as authored.
					return JsonValue.Create(text);
				}
				if (!columns.Contains(name, StringComparer.Ordinal)) {
					columns.Add(name);
				}
				return JsonValue.Create(BindingPrefix + LegacyMobileListAnalysisService.AttributeName(name));
			}
			default:
				return node?.DeepClone();
		}
	}

	/// <summary>Folds one element's override values over anything already recorded for it; the later key wins.</summary>
	private static void Fold(
		Dictionary<string, JsonObject> elementValues, string name, JsonObject values, bool appendArrays = false) {
		if (!elementValues.TryGetValue(name, out JsonObject existing)) {
			elementValues[name] = values;
			return;
		}
		foreach (KeyValuePair<string, JsonNode> pair in values) {
			if (appendArrays && existing[pair.Key] is JsonArray target && pair.Value is JsonArray addition) {
				foreach (JsonNode item in addition) {
					target.Add(item?.DeepClone());
				}
				continue;
			}
			existing[pair.Key] = pair.Value?.DeepClone();
		}
	}

	private static LegacyGridPageSettings DropColumn(LegacyGridPageSettings settings, string bucket, string columnPath) {
		bool Keep(LegacyGridColumn c) => !string.Equals(c.ColumnName, columnPath, StringComparison.Ordinal);
		return bucket switch {
			LegacyGridPageSettingsParser.ItemsBucket => settings with { Items = [.. settings.Items.Where(Keep)] },
			LegacyGridPageSettingsParser.SubtitleItemsBucket => settings with { SubtitleItems = [.. settings.SubtitleItems.Where(Keep)] },
			LegacyGridPageSettingsParser.GroupItemsBucket => settings with { GroupItems = [.. settings.GroupItems.Where(Keep)] },
			_ => settings
		};
	}

	private static JsonObject Remove(string name) => new() { ["operation"] = "remove", ["name"] = name };

	private static LegacyOverrideOutcome Report(string section, Operation op, string reason) =>
		new(section, op.Index, op.Kind, op.Name, LegacyOverrideLanes.Reported, null, reason);

	/// <summary>Mirrors the mobile applier's grouping so two operations on one target compose as they do at runtime.</summary>
	private static int OperationRank(string kind) {
		int index = Array.FindIndex(RuntimeOperationOrder, o => string.Equals(o, kind, StringComparison.OrdinalIgnoreCase));
		return index < 0 ? RuntimeOperationOrder.Length : index;
	}

	private static string ResolveSegment(string segment, MobileLegacyTemplateRule template) => segment switch {
		ItemsPlaceholder => template.ItemsAttributeName,
		PrimaryDataSourcePlaceholder => PrimaryDataSourceAlias,
		_ => segment
	};

	private static MobileLegacyRuntimeAnchorRule Rule(Dictionary<string, MobileLegacyRuntimeAnchorRule> byRole, string role) =>
		role is not null && byRole.TryGetValue(role, out MobileLegacyRuntimeAnchorRule rule) ? rule : null;

	private static Dictionary<string, MobileLegacyRuntimeAnchorRule> BuildRoleIndex(MobileLegacyRuntimeNameSet nameSet) {
		var byRole = new Dictionary<string, MobileLegacyRuntimeAnchorRule>(StringComparer.Ordinal);
		foreach (MobileLegacyRuntimeAnchorRule rule in nameSet?.Anchors ?? []) {
			if (!string.IsNullOrWhiteSpace(rule?.Role)) {
				byRole.TryAdd(rule.Role, rule);
			}
		}
		return byRole;
	}

	/// <summary>Reads a section's operations into a shape the lanes can reason about, preserving source order.</summary>
	private static List<Operation> Read(LegacyOverrideSection section) {
		var operations = new List<Operation>();
		for (int i = 0; i < section.Operations.Count; i++) {
			if (section.Operations[i] is not JObject item) {
				continue;
			}
			operations.Add(new Operation(
				section.Section,
				i,
				item.Value<string>("operation") ?? string.Empty,
				item.Value<string>("name"),
				item["values"] as JObject,
				[.. (item["properties"] as JArray ?? []).Select(p => p.ToString())]));
		}
		return operations;
	}

	private static JsonNode ToJsonNode(JObject values) => JsonNode.Parse(values.ToString(Newtonsoft.Json.Formatting.None));

	/// <summary>One override operation, flattened.</summary>
	private sealed record Operation(
		string Section, int Index, string Kind, string Name, JObject Values, IReadOnlyList<string> Properties);

	/// <summary>What an operation resolves to, before anything is applied.</summary>
	private sealed record Resolution(
		bool Resolved, string DropColumnFrom, string AddColumnTo, LegacyGridColumn AddedColumn,
		IReadOnlyList<JsonObject> TargetOperations,
		string ElementName, JsonObject ElementValues, IReadOnlyList<string> RequiredColumns,
		string Effect, string Reason, string Warning) {

		/// <summary>Whether an array value adds to what is already recorded instead of replacing it.</summary>
		public bool AppendArrays { get; init; }

		public static Resolution No(string reason) =>
			new(false, null, null, null, [], null, null, [], null, reason, null);

		public static Resolution Source(string bucket, string effect) =>
			new(true, bucket, null, null, [], null, null, [], effect, null, null);

		/// <summary>The column the override asked for is added to the wizard model, with a warning about the slot.</summary>
		public static Resolution Add(string bucket, LegacyGridColumn column, string effect, string warning) =>
			new(true, null, bucket, column, [], null, null, [], effect, null, warning);

		/// <summary>The operation is understood and needs no change: the converted page already has that outcome.</summary>
		public static Resolution Unchanged(string effect, string warning) =>
			new(true, null, null, null, [], null, null, [], effect, null, warning);

		public static Resolution Target(IReadOnlyList<JsonObject> operations, string effect) =>
			new(true, null, null, null, operations, null, null, [], effect, null, null);

		public static Resolution Element(string name, JsonObject values, IReadOnlyList<string> columns, string effect) =>
			new(true, null, null, null, [], name, values, columns, effect, null, null);

		/// <summary>
		/// One entry the override ADDS to a collection property of a template element. Several such operations
		/// accumulate into one merge, so their arrays are appended rather than replacing one another.
		/// </summary>
		public static Resolution ElementEntry(
			string name, JsonObject values, IReadOnlyList<string> columns, string effect, string warning) =>
			new(true, null, null, null, [], name, values, columns, effect, null, warning) { AppendArrays = true };
	}
}

