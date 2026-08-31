namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Detects pairs of <c>viewConfigDiff</c> operations that target one component <c>name</c> in a single
/// body where the differ discards one of them, and reports them as advisory warnings.
/// </summary>
/// <remarks>
/// A <c>viewConfigDiff</c> reads as an ordered operation list, but <see cref="JsonDiffApplier"/> — clio's
/// clone of the platform differ — does not apply it in array order. It splits the array into groups and
/// applies whole groups in a fixed sequence (<c>ApplyOperations</c>): merges, then the
/// remove/insert/move position pipeline, then property removals, then <c>set</c>. Ordering the array
/// therefore changes nothing, and two operations that read as composing can silently cancel one another.
/// <para>
/// Three mechanisms discard an operation without any diagnostic, and every rule below rests on one of
/// them:
/// <list type="bullet">
/// <item>group ordering — a later group has already destroyed, or has yet to create, what an earlier
/// group's operation needs;</item>
/// <item><c>FilterMoveOperation</c>, which drops every <c>move</c> whose name matches an element
/// <c>remove</c> in the same body, before anything is applied;</item>
/// <item>source resolution — an operation whose target name is absent resolves to nothing and is
/// skipped, and <c>ApplyOperations</c> throws away the unsuccessful list every group returns.</item>
/// </list>
/// </para>
/// All findings are WARNINGS, not errors, for the same reason
/// <see cref="PageInsertDowngradeDetector"/>'s are: this detector reads ONE schema body and cannot see
/// the replacing chain. A parent schema inserting the same name puts the component in the base, which
/// for the three conditional rules below can make the transform apply after all; and an ancestor's
/// <c>alias</c> declaration carrying <c>excludeOperations</c> can legitimately neutralise an operation
/// (<c>JsonDiffApplier.IsExcludeAliasOperation</c>). Neither is visible here, so it advises rather than
/// blocks.
/// <para>
/// Operation verbs are compared <see cref="StringComparer.Ordinal"/> and are never case-folded. This is
/// load-bearing, not an oversight: the differ switches on the raw verb string with no <c>default</c>
/// branch, so a mis-cased <c>"Merge"</c> lands in no group and is discarded whole. Treating it as a live
/// <c>merge</c> here would report a pair that does not exist. Reporting the mis-cased verb itself is a
/// separate finding and deliberately out of scope — GH-1240 covers the pairs.
/// </para>
/// Array positions are not recorded and not reported, because the differ ignores them: a finding names
/// the component and the two verbs, which is the whole of what the author can act on. One known blind
/// spot, in the fail-quiet direction: an <c>insert</c> that carries its element's <c>name</c> inside
/// <c>values</c> rather than on the operation object still creates that component (the differ only
/// stamps the operation-level name when it is non-empty), but has no name to group on here, so a
/// sibling operation for it goes unreported.
/// <para>
/// The detector needs no knowledge of which mode produced the body — it inspects the resolved final
/// body, so it covers <c>replace</c> and <c>append</c> identically, and applies to <c>sync-pages</c>
/// (which pins <c>replace</c>) as much as to <c>update-page</c>.
/// </para>
/// </remarks>
internal static class PageInertOperationDetector {

	private const string ViewConfigDiffMarker = "SCHEMA_VIEW_CONFIG_DIFF";

	// Legacy spelling of the same section, still accepted by every other reader in the repo
	// (PageSchemaBodyParser, ChartConfigKeyOrderPreprocessor, SchemaValidationService). Reading only the
	// modern marker would make this check a silent no-op on a whole body dialect.
	private const string LegacyViewConfigDiffMarker = "SCHEMA_DIFF";

	private const string ViewConfigDiffProperty = "viewConfigDiff";
	private const string GuidePointer = " See docs://mcp/guides/page-modification.";

	/// <summary>
	/// Hard ceiling on reported findings, deliberately well below the worst case, so a pathological body
	/// cannot bury the response. Beyond it one summary line names the remainder rather than hiding it.
	/// </summary>
	private const int MaxReportedFindings = 12;

	/// <summary>
	/// The group a verb lands in, transcribed from <c>JsonDiffApplier.GetSplittedOperations</c>.
	/// <see cref="Dropped"/> is that switch's MISSING <c>default</c> branch: an unknown or mis-cased verb
	/// matches no case, lands in no group, and is discarded whole with no exception.
	/// </summary>
	private enum ApplyGroup {

		Merge,
		Set,
		Insert,
		Move,
		ElementRemove,
		PropertyRemove,
		Dropped

	}

	/// <summary>
	/// One rule: when <c>Live</c> and <c>Discarded</c> both target one name, <c>Discarded</c> does not
	/// take effect, and <c>Message</c> says so. <c>RescuedBy</c>, when set, is a third group whose
	/// presence makes <c>Discarded</c> effective after all and therefore suppresses the rule.
	/// </summary>
	/// <remarks>
	/// The message is carried in the row rather than reached through a shape enum and a parallel switch,
	/// so a new rule cannot be added without its message — and so no unmatched-case throw exists on a
	/// path whose whole contract is that it must never affect a save.
	/// </remarks>
	private readonly record struct InertRule(
		ApplyGroup Live,
		ApplyGroup Discarded,
		Func<string, string> Message,
		ApplyGroup? RescuedBy = null);

	/// <summary>
	/// Every co-occurrence of two apply groups for ONE name where the differ discards one of them. Each
	/// row is checkable against <c>JsonDiffApplier</c> on its own, without reading any control flow here;
	/// the citation in the comment above it is the proof.
	/// <para>
	/// The first four rows hold UNCONDITIONALLY — the discard follows from group order alone, whether or
	/// not the component exists in the base. The last three involve this body's own <c>insert</c> and are
	/// conditional on the component NOT coming from a parent schema in the replacing chain, which this
	/// detector cannot see; their messages state that case rather than asserting the author is wrong.
	/// Unconditional rows come first because that order is also the dedupe precedence: at most one
	/// finding per (name, discarded group), so the strongest available reason is the one reported.
	/// </para>
	/// <para>
	/// Two shapes that look like they belong here are absent, because reading the applier shows the named
	/// operation still does something. <c>insert</c> + <c>set</c>: <c>Set</c> removes the element first
	/// and copies the removed item's <c>index</c> and <c>propertyName</c> back onto its own config, so
	/// the insert's POSITION survives as an override — note it keeps only those two, taking
	/// <c>parentName</c> from the set itself, and the insert's <c>values</c> are discarded wholesale.
	/// <c>merge</c> + property <c>remove</c>: <c>Remove</c>'s property branch deletes only the NAMED
	/// properties and <c>Merge</c> writes only the keys present in <c>values</c>, so only the
	/// intersection of the two key sets is lost, not the merge. A third near-miss, <c>set</c> +
	/// <c>move</c>, is excluded for the same reason: <c>Set</c> re-inserts under its OWN
	/// <c>parentName</c>, which overrides the move when the two disagree but is not a discard.
	/// </para>
	/// </summary>
	private static readonly InertRule[] InertRules = [
		// FilterMoveOperation opens the position pipeline and drops every move whose name matches any
		// element remove — unconditionally, whether or not the remove itself resolves.
		new(ApplyGroup.ElementRemove, ApplyGroup.Move, BuildMoveBesideElementRemoveMessage),
		// The merge group patches the element; the position pipeline then deletes it. An insert of the
		// same name does not rescue the merge: the insert builds a fresh element from its own values.
		new(ApplyGroup.ElementRemove, ApplyGroup.Merge, BuildMergeBesideElementRemoveMessage),
		// The set group runs last and replaces the element wholesale with its own values.
		new(ApplyGroup.Set, ApplyGroup.Merge, BuildMergeBesideSetMessage),
		// Element removals run in the position pipeline, property removals in the group after it, so the
		// property removal targets an element that is already gone — UNLESS an insert for the same name
		// re-creates it in that same pipeline, which runs before the property-removal group and makes the
		// property removal effective. Hence RescuedBy.
		new(ApplyGroup.ElementRemove, ApplyGroup.PropertyRemove, BuildPropertyRemoveBesideElementRemoveMessage,
			RescuedBy: ApplyGroup.Insert),
		// The merge group runs before the position pipeline, so a merge aimed at a component this body
		// inserts resolves against a base that does not contain it, fails, and its unsuccessful list is
		// discarded. Conditional: a parent schema inserting the name puts it in the base.
		new(ApplyGroup.Insert, ApplyGroup.Merge, BuildMergeBesideInsertMessage),
		// Moves are resolved against the pristine source before any insert runs, and an unresolved move
		// yields neither a remove nor a generated insert — it vanishes entirely. Conditional in the same
		// way: with the name in the base the move resolves, and the own insert then duplicates the name.
		new(ApplyGroup.Insert, ApplyGroup.Move, BuildMoveBesideInsertMessage),
		// The position pipeline applies ALL removes before ANY insert, so a remove cannot delete what the
		// same body inserts. Conditional, and the loosest row: with the name in the base this is the
		// legitimate replace-an-inherited-component idiom, where both operations do apply.
		new(ApplyGroup.Insert, ApplyGroup.ElementRemove, BuildElementRemoveBesideInsertMessage)
	];

	/// <summary>
	/// Inspects the body about to be written and returns one advisory warning per component and dropped
	/// operation, for the operations the differ discards at apply time.
	/// </summary>
	/// <param name="body">
	/// Resolved final page body (post-merge for <c>append</c>, verbatim for <c>replace</c>). May be
	/// <c>null</c> or empty, in which case nothing is reported.
	/// </param>
	/// <returns>
	/// A read-only list of warning messages, empty when nothing is found and never <c>null</c>. Capped at
	/// <see cref="MaxReportedFindings"/> findings plus one line naming how many were not listed.
	/// </returns>
	public static IReadOnlyList<string> Detect(string body) {
		var warnings = new List<string>();
		if (string.IsNullOrWhiteSpace(body)) {
			return warnings;
		}
		if (!TryExtractGroupsByName(body, out List<string> namesInOrder,
			out Dictionary<string, HashSet<ApplyGroup>> groupsByName)) {
			// Unparseable body — fail open. An advisory check must never affect a save.
			return warnings;
		}
		int suppressed = 0;
		var reportedForName = new HashSet<ApplyGroup>();
		foreach (string name in namesInOrder) {
			HashSet<ApplyGroup> groups = groupsByName[name];
			if (groups.Count < 2) {
				continue;
			}
			reportedForName.Clear();
			foreach (InertRule rule in InertRules) {
				if (!groups.Contains(rule.Live) || !groups.Contains(rule.Discarded)) {
					continue;
				}
				if (rule.RescuedBy is { } rescuer && groups.Contains(rescuer)) {
					continue;
				}
				// One finding per dropped operation, not per rule: several rules can name the same dead
				// operation for different reasons, and repeating it says nothing new. Rules are ordered
				// strongest-reason-first, so the first match is the one worth reporting.
				if (!reportedForName.Add(rule.Discarded)) {
					continue;
				}
				if (warnings.Count < MaxReportedFindings) {
					warnings.Add(rule.Message(name));
				} else {
					suppressed++;
				}
			}
		}
		if (suppressed > 0) {
			warnings.Add(BuildSuppressedMessage(suppressed));
		}
		return warnings;
	}

	/// <summary>
	/// Maps a verb to its apply group, transcribed from <c>JsonDiffApplier.GetSplittedOperations</c>.
	/// The <c>remove</c> split on a <c>properties</c> ARRAY is the differ's own discriminator: a
	/// <c>properties</c> value that is not an array is an element removal, not a property removal.
	/// </summary>
	private static ApplyGroup Classify(string verb, JToken properties) => verb switch {
		"merge" => ApplyGroup.Merge,
		"set" => ApplyGroup.Set,
		"insert" => ApplyGroup.Insert,
		"move" => ApplyGroup.Move,
		"remove" => properties is JArray ? ApplyGroup.PropertyRemove : ApplyGroup.ElementRemove,
		_ => ApplyGroup.Dropped
	};

	// Messages follow the convention of PageInsertDowngradeDetector's: what is wrong, why it matters at
	// runtime, what to do instead, then the guide pointer. They say "submitted body" rather than "body
	// being saved" because Detect also runs on a dry run, where nothing is saved.

	private static string BuildMoveBesideElementRemoveMessage(string name) =>
		$"Component '{name}' carries both an element 'remove' and a 'move' in the submitted body. " +
		"Before applying anything the differ filters the move list against the remove list and drops " +
		"every move whose name matches a remove — unconditionally, whether or not the remove itself " +
		"resolves. The component is deleted and the move never runs. Drop the remove if you meant to " +
		"relocate the component." + GuidePointer;

	private static string BuildMergeBesideElementRemoveMessage(string name) =>
		$"Component '{name}' carries both a 'merge' and an element 'remove' in the submitted body. " +
		"Merges are applied first and removes second, so the merge patches the element and the remove " +
		"then deletes it: the merged values never reach runtime. Drop whichever of the two you did not " +
		"intend." + GuidePointer;

	private static string BuildMergeBesideSetMessage(string name) =>
		$"Component '{name}' carries both a 'merge' and a 'set' in the submitted body. 'set' is applied " +
		"last and replaces the element wholesale with its own 'values', so the merge's values are " +
		"overwritten and never reach runtime. Fold the merge's values into the set's 'values'." +
		GuidePointer;

	private static string BuildPropertyRemoveBesideElementRemoveMessage(string name) =>
		$"Component '{name}' carries both an element 'remove' and a property 'remove' (one with a " +
		"'properties' array) in the submitted body. Element removals are applied in the group before " +
		"property removals, so by the time the property removal runs the element is gone and it does " +
		"nothing. Drop the property removal, or drop the element removal if you only meant to strip " +
		"properties." + GuidePointer;

	private static string BuildMergeBesideInsertMessage(string name) =>
		$"Component '{name}' carries both an 'insert' and a 'merge' in the submitted body. The differ " +
		"applies whole operation groups in a fixed order — merges run BEFORE inserts, never in array " +
		"order — so the merge resolves against a base that does not contain the component yet and is " +
		"silently dropped. Fold the merge's values into the insert's own 'values', or use 'set', the " +
		"only verb applied after inserts. If a parent schema inserts this name too, the merge patches " +
		"the parent's element and this body's insert then adds a second component with the same name." +
		GuidePointer;

	private static string BuildMoveBesideInsertMessage(string name) =>
		$"Component '{name}' carries both an 'insert' and a 'move' in the submitted body. Moves are " +
		"resolved against the unmodified base before any insert runs, so a move aimed at a component " +
		"the same body inserts resolves to nothing and is discarded entirely — not even the relocation " +
		"survives. Set the insert's own 'parentName' and 'index' instead of adding a move. If a parent " +
		"schema inserts this name too, the move relocates the parent's element and this body's insert " +
		"then adds a second component with the same name." + GuidePointer;

	private static string BuildElementRemoveBesideInsertMessage(string name) =>
		$"Component '{name}' carries both an 'insert' and an element 'remove' in the submitted body. If " +
		"the component comes from a parent schema this is the replace-an-inherited-component idiom and " +
		"both operations apply — 'set' expresses it in one operation. If the component is instead " +
		"self-inserted, the remove does nothing at all: within a single body ALL removes are applied " +
		"before ANY insert, so the remove can never delete what this body inserts. Prefer 'set', or " +
		"drop whichever of the two is redundant." + GuidePointer;

	private static string BuildSuppressedMessage(int suppressed) =>
		$"{suppressed} further inert-operation finding(s) in this body are not listed. Fix the ones above " +
		"and re-run to see the rest." + GuidePointer;

	/// <summary>
	/// Reduces the body's <c>viewConfigDiff</c> to the set of apply groups present per component name,
	/// plus the order the names first appear in, so findings are reported deterministically.
	/// </summary>
	/// <returns><c>false</c> when the body cannot be parsed, so the caller can fail open.</returns>
	private static bool TryExtractGroupsByName(string body, out List<string> namesInOrder,
		out Dictionary<string, HashSet<ApplyGroup>> groupsByName) {
		namesInOrder = [];
		// Names use Ordinal to mirror the differ's own per-name grouping
		// (JsonDiffApplier.GetObjectNameOperationsGroup) and the name half of PageBodyMerger's merge
		// identity. Verbs are classified Ordinal too — see the class remarks; that is not negotiable.
		groupsByName = new Dictionary<string, HashSet<ApplyGroup>>(StringComparer.Ordinal);
		try {
			JArray viewConfigDiff = ReadViewConfigDiff(body);
			foreach (JToken item in viewConfigDiff) {
				if (item is not JObject obj) {
					continue;
				}
				// The name must be a JSON STRING, matching PageBodyMerger.TryGetOperationIdentity: a bare
				// ToString() would let {"name":123} and {"name":"123"} share an identity.
				if (obj["name"] is not JValue { Type: JTokenType.String } nameValue) {
					continue;
				}
				string name = nameValue.Value<string>();
				string verb = (obj["operation"] as JValue)?.Value<string>();
				if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(verb)) {
					// A missing operation is an ERROR the differ throws on, not an inert operation.
					continue;
				}
				ApplyGroup group = Classify(verb, obj["properties"]);
				if (group == ApplyGroup.Dropped) {
					// The differ discards this entry whole; it can neither cancel nor be cancelled.
					continue;
				}
				if (!groupsByName.TryGetValue(name, out HashSet<ApplyGroup> groups)) {
					groups = [];
					groupsByName[name] = groups;
					namesInOrder.Add(name);
				}
				groups.Add(group);
			}
			return true;
		} catch (JsonException) {
			// Unparseable body — fail open (skip the heuristic, never block the save).
			return false;
		} catch (RegexMatchTimeoutException) {
			// A pathological body tripped the section-reader regex timeout — fail open as well,
			// otherwise this advisory check would block an otherwise-valid save.
			return false;
		}
	}

	private static JArray ReadViewConfigDiff(string body) {
		if (PageSchemaTypeExtensions.FromBody(body) == PageSchemaType.Mobile) {
			JObject json = JObject.Parse(body);
			return json[ViewConfigDiffProperty] as JArray ?? new JArray();
		}
		if (!PageSchemaSectionReader.TryRead(body, out string content, ViewConfigDiffMarker,
			LegacyViewConfigDiffMarker)) {
			return new JArray();
		}
		string trimmed = content.Trim();
		return string.IsNullOrEmpty(trimmed) || trimmed == "[]" ? new JArray() : JArray.Parse(trimmed);
	}
}
