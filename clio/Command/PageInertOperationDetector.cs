namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Detects pairs of <c>viewConfigDiff</c> operations that target one component <c>name</c> in a single
/// body where the differ provably discards one of them, and reports them as advisory warnings.
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
/// the replacing chain. A parent schema inserting the same name puts the component in the base and can
/// make a transform apply after all, and an ancestor's <c>alias</c> declaration carrying
/// <c>excludeOperations</c> can legitimately neutralise an operation
/// (<c>JsonDiffApplier.IsExcludeAliasOperation</c>) — neither is visible here. It therefore advises
/// rather than blocks.
/// <para>
/// Operation verbs are compared <see cref="StringComparer.Ordinal"/> and are never case-folded. This is
/// load-bearing, not an oversight: the differ switches on the raw verb string with no <c>default</c>
/// branch, so a mis-cased <c>"Merge"</c> lands in no group and is discarded whole. Treating it as a live
/// <c>merge</c> here would report a pair that does not exist. Reporting the mis-cased verb itself is a
/// separate finding and deliberately out of scope — GH-1240 covers the pairs.
/// </para>
/// Array positions are not recorded and not reported, because the differ ignores them: a finding names
/// the component and the two verbs, which is the whole of what the author can act on.
/// <para>
/// The detector needs no knowledge of which mode produced the body — it inspects the resolved final
/// body, so it covers <c>replace</c> and <c>append</c> identically, and applies to <c>sync-pages</c>
/// (which pins <c>replace</c>) as much as to <c>update-page</c>.
/// </para>
/// </remarks>
internal static class PageInertOperationDetector {

	private const string ViewConfigDiffMarker = "SCHEMA_VIEW_CONFIG_DIFF";
	private const string ViewConfigDiffProperty = "viewConfigDiff";
	private const string GuidePointer = " See docs://mcp/guides/page-modification.";

	/// <summary>
	/// Upper bound on reported findings, so a pathological body cannot bury the response in warnings.
	/// A page carrying ~40 operations bounds the distinct pairs at roughly 20; beyond this cap one
	/// summary line names the remainder rather than hiding it.
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

	/// <summary>Which co-occurrence was found — one value per row of <see cref="InertPairs"/>.</summary>
	private enum InertShape {

		MergeBesideInsert,
		MoveBesideInsert,
		ElementRemoveBesideInsert,
		MoveBesideElementRemove,
		MergeBesideElementRemove,
		MergeBesideSet,
		PropertyRemoveBesideElementRemove

	}

	/// <summary>
	/// Every co-occurrence of two apply groups for ONE name that provably discards one of them. Each row
	/// is checkable against <c>JsonDiffApplier</c> on its own, without reading any control flow here; the
	/// citation in the comment above it is the proof.
	/// <para>
	/// Two shapes that look like they belong here are deliberately absent, because reading the applier
	/// shows they are not inert. <c>insert</c> + <c>set</c>: <c>Set</c> removes first and copies the
	/// removed item's <c>index</c> and <c>propertyName</c> back onto its own config before inserting, so
	/// the insert establishes the existence and position the set then reuses — only its <c>values</c> are
	/// overwritten. <c>merge</c> + property <c>remove</c>: <c>Remove</c>'s property branch deletes only
	/// the NAMED properties and <c>Merge</c> writes only the keys present in <c>values</c>, so only the
	/// intersection of the two key sets is lost, not the merge.
	/// </para>
	/// </summary>
	private static readonly (ApplyGroup Live, ApplyGroup Discarded, InertShape Shape)[] InertPairs = [
		// The merge group runs before the position pipeline, so the merge resolves against a base that
		// does not contain the component yet, fails, and its unsuccessful list is discarded.
		(ApplyGroup.Insert, ApplyGroup.Merge, InertShape.MergeBesideInsert),
		// Moves are resolved against the pristine source before any insert runs, and an unresolved move
		// yields neither a remove nor a generated insert — it vanishes entirely.
		(ApplyGroup.Insert, ApplyGroup.Move, InertShape.MoveBesideInsert),
		// The position pipeline applies ALL removes before ANY insert, so a remove cannot delete what the
		// same body inserts.
		(ApplyGroup.Insert, ApplyGroup.ElementRemove, InertShape.ElementRemoveBesideInsert),
		// FilterMoveOperation opens the position pipeline and drops every move whose name matches any
		// element remove — unconditionally, whether or not the remove itself resolves.
		(ApplyGroup.ElementRemove, ApplyGroup.Move, InertShape.MoveBesideElementRemove),
		// The merge group patches the element; the position pipeline then deletes it.
		(ApplyGroup.ElementRemove, ApplyGroup.Merge, InertShape.MergeBesideElementRemove),
		// The set group runs last and replaces the element wholesale with its own values.
		(ApplyGroup.Set, ApplyGroup.Merge, InertShape.MergeBesideSet),
		// Element removals run in the position pipeline, property removals in the group after it, so the
		// property removal targets an element that is already gone.
		(ApplyGroup.ElementRemove, ApplyGroup.PropertyRemove, InertShape.PropertyRemoveBesideElementRemove)
	];

	/// <summary>
	/// Inspects the body about to be written and returns one advisory warning per component and shape
	/// whose second operation the differ discards at apply time.
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
		foreach (string name in namesInOrder) {
			HashSet<ApplyGroup> groups = groupsByName[name];
			if (groups.Count < 2) {
				continue;
			}
			foreach ((ApplyGroup live, ApplyGroup discarded, InertShape shape) in InertPairs) {
				if (!groups.Contains(live) || !groups.Contains(discarded)) {
					continue;
				}
				if (warnings.Count < MaxReportedFindings) {
					warnings.Add(BuildMessage(shape, name));
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

	private static string BuildMessage(InertShape shape, string name) => shape switch {
		InertShape.MergeBesideInsert =>
			$"Component '{name}' carries both an 'insert' and a 'merge' in the body being saved. The differ " +
			"applies whole operation groups in a fixed order — merges run BEFORE inserts, never in array " +
			"order — so the merge resolves against a base that does not contain the component yet and is " +
			"silently dropped. Fold the merge's values into the insert's own 'values', or use 'set', the " +
			"only verb applied after inserts. If a parent schema inserts this name too, the merge patches " +
			"the parent's element and this body's insert then adds a second component with the same name." +
			GuidePointer,
		InertShape.MoveBesideInsert =>
			$"Component '{name}' carries both an 'insert' and a 'move' in the body being saved. Moves are " +
			"resolved against the unmodified base before any insert runs, so a move aimed at a component " +
			"the same body inserts resolves to nothing and is discarded entirely — not even the relocation " +
			"survives. Set the insert's own 'parentName' and 'index' instead of adding a move." +
			GuidePointer,
		InertShape.ElementRemoveBesideInsert =>
			$"Component '{name}' carries both an 'insert' and an element 'remove' in the body being saved. " +
			"If the component comes from a parent schema this is the replace-an-inherited-component idiom, " +
			"and 'set' expresses it in one operation. Within a single body, though, ALL removes are applied " +
			"before ANY insert, so the remove can never delete what this body inserts: if the component is " +
			"self-inserted the remove does nothing at all. Prefer 'set', or drop whichever of the two is " +
			"redundant." + GuidePointer,
		InertShape.MoveBesideElementRemove =>
			$"Component '{name}' carries both an element 'remove' and a 'move' in the body being saved. " +
			"Before applying anything the differ filters the move list against the remove list and drops " +
			"every move whose name matches a remove — unconditionally, whether or not the remove itself " +
			"resolves. The component is deleted and the move never runs. Drop the remove if you meant to " +
			"relocate the component." + GuidePointer,
		InertShape.MergeBesideElementRemove =>
			$"Component '{name}' carries both a 'merge' and an element 'remove' in the body being saved. " +
			"Merges are applied first and removes second, so the merge patches the element and the remove " +
			"then deletes it: the merged values never reach runtime. Drop whichever of the two you did not " +
			"intend." + GuidePointer,
		InertShape.MergeBesideSet =>
			$"Component '{name}' carries both a 'merge' and a 'set' in the body being saved. 'set' is " +
			"applied last and replaces the element wholesale with its own 'values', so the merge's values " +
			"are overwritten and never reach runtime. Fold the merge's values into the set's 'values'." +
			GuidePointer,
		InertShape.PropertyRemoveBesideElementRemove =>
			$"Component '{name}' carries both an element 'remove' and a property 'remove' (one with a " +
			"'properties' array) in the body being saved. Element removals are applied in the group before " +
			"property removals, so by the time the property removal runs the element is gone and it does " +
			"nothing. Drop the property removal, or drop the element removal if you only meant to strip " +
			"properties." + GuidePointer,
		_ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unhandled inert shape.")
	};

	private static string BuildSuppressedMessage(int suppressed) =>
		$"{suppressed} further inert-operation finding(s) in this body are not listed. Fix the ones above " +
		"and save again to see the rest." + GuidePointer;

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
		if (!PageSchemaSectionReader.TryRead(body, out string content, ViewConfigDiffMarker)) {
			return new JArray();
		}
		string trimmed = content.Trim();
		return string.IsNullOrEmpty(trimmed) || trimmed == "[]" ? new JArray() : JArray.Parse(trimmed);
	}
}
