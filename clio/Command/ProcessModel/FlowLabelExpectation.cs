using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Post-operation check that a flow <c>label</c> the caller SENT was actually APPLIED by the server.
/// <para>The failure is silent, and silent in the ordinary direction rather than a corner: a
/// <c>CrtProcessBuilder</c> below <see cref="MinimumPackageVersion"/> declares no <c>label</c> member on its
/// flow descriptor, so its <c>DataContractJsonSerializer</c> DISCARDS the field and the operation still
/// answers <c>success:true</c>. The caller gets the two identical unlabelled arrows the label exists to
/// prevent, while every signal they can see says the write worked.</para>
/// <para>Behavioural rather than version-based, for the reasons <see cref="EmailBlockExpectation"/> records:
/// a floor states what an environment should support, a read-back states what it did, and nothing here has to
/// be revisited when the bundled version moves.</para>
/// <para>A label is addressed by its flow's ENDPOINT PAIR, because that is the only handle the write side
/// gives — <c>flows[]</c>, <c>addFlow</c> and <c>setFlow</c> all name a source and a target, and the flow's
/// own generated NAME is derived from its kind, which the server is allowed to normalise out from under the
/// caller. Matching on the pair therefore survives a normalisation that a name match would report as a
/// dropped label.</para>
/// <para>The checks are pure so they can be tested without a server; the describe round trip belongs to the
/// commands, which own the <see cref="IProcessDescriber"/> dependency.</para>
/// </summary>
public static class FlowLabelExpectation {

	// Descriptor / operation JSON keys, named once so the parsing shape reads consistently.
	private const string FlowsKey = "flows";
	private const string LabelKey = "label";
	private const string SourceKey = "source";
	private const string TargetKey = "target";
	private const string OpKey = "op";

	/// <summary>
	/// The first <c>CrtProcessBuilder</c> that carries a flow <c>label</c> at all.
	/// <para>Named ONCE and read by both the warning text and its test, because the same four digits appear
	/// in this repository with the opposite lifecycle: <c>ExpectedArchiveVersion</c> in the bundled-package
	/// guard fixture is a pin on the archive that currently ships and MUST move on every rebundle, while this
	/// is the version a CAPABILITY arrived in and must not move at all. Two identical literals whose correct
	/// behaviour diverges is a grep-and-replace waiting to corrupt a caller-facing message.</para>
	/// <para>It is also the one thing this guard family names a number for. The three sibling guards describe
	/// their cause qualitatively ("predates the sendEmail element") and carry no version, deliberately; a
	/// label's cause is a single archive and saying which one turns "it did not work" into a diagnosis.</para>
	/// </summary>
	internal const string MinimumPackageVersion = "1.6.0.8";

	// How much caller-supplied text a warning will echo. Long enough that a real label is never cut - the
	// shipped corpus's longest is well under it - and short enough that a pathological one cannot bloat the
	// message an agent reads.
	private const int MaxRenderedTextLength = 200;

	/// <summary>The modify operations that WRITE a flow label. Nothing else on the operations array does.</summary>
	private static readonly string[] LabelWritingOperations = ["addFlow", "setFlow"];

	/// <summary>One flow the caller asked to label, addressed the only way the write side allows.</summary>
	/// <param name="Source">Source element name, trimmed the way the server trims it before resolving it.</param>
	/// <param name="Target">Target element name, trimmed the same way.</param>
	/// <param name="Label">
	/// The label text that was sent, trimmed. An EMPTY string is a deliberate CLEAR and is carried as one:
	/// see <see cref="MissingLabels"/> for the asymmetry in how the two are verified.
	/// </param>
	public sealed record FlowLabel(string Source, string Target, string Label);

	/// <summary>
	/// The labels a BUILD descriptor asks for — every <c>flows[]</c> entry carrying a <c>label</c> member.
	/// Returns an empty list when no flow mentioned one, which skips the verification entirely.
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<FlowLabel> FromDescriptor(string descriptorJson) {
		JsonObject? descriptor = BlockExpectationJson.Parse(descriptorJson) as JsonObject;
		// No op filter: every entry under flows[] is a flow declaration, and the build path refuses a
		// duplicate endpoint pair, so there is nothing to supersede either.
		return descriptor?[FlowsKey] is JsonArray flows
			? CollectLabelled(flows, filterByOp: false)
			: Array.Empty<FlowLabel>();
	}

	/// <summary>
	/// The labels a MODIFY operations array asks for. Both write routes carry the field on the operation
	/// itself — <c>addFlow</c> and <c>setFlow</c> each take <c>source</c>, <c>target</c> and <c>label</c> — so
	/// unlike the email block there is no nested descriptor to reach into.
	/// <para>Only those two operations are read. <c>label</c> is a member of the SHARED operation descriptor,
	/// so it deserializes on <c>setFlowCondition</c> and <c>removeFlow</c> too and is ignored there — and
	/// collecting it would make this guard blame the package for a field the operation never uses, with a
	/// remedy (update the package) that cannot work.</para>
	/// </summary>
	/// <param name="operationsJson">The operations array JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<FlowLabel> FromOperations(string operationsJson) =>
		BlockExpectationJson.Parse(operationsJson) is JsonArray operations
			? CollectLabelled(operations, filterByOp: true)
			: Array.Empty<FlowLabel>();

	/// <summary>
	/// Of the labels that were sent, those the read-back shows are NOT on the flow.
	/// <para>A flow the description does not contain is NOT reported. The endpoint pair may have been written
	/// as UIds rather than names, or the flow may have been removed later in the same batch, and neither is
	/// evidence that a label was dropped — the guard's whole value is that it does not cry wolf on a build
	/// that worked.</para>
	/// <para>A label that came back DIFFERENT is reported too, not just an absent one — anything other than
	/// the text that was asked for is a real disagreement between the request and what is drawn.</para>
	/// <para>A CLEAR is verified ASYMMETRICALLY, and the asymmetry is the whole reason it can be verified at
	/// all. An empty label asks for no label, and "no label" is also what a server that DISCARDED the field
	/// leaves behind on a flow that never had one — so a read-back showing nothing proves nothing, and this
	/// stays silent. But on the modify path the flow normally already carries a label, and there a read-back
	/// showing the OLD text is positive proof the clear did not land. That case is reported. (It is reported
	/// as the empty label the caller sent, so the warning shows <c>('')</c> — which is what they asked
	/// for.)</para>
	/// </summary>
	/// <param name="described">The description read back after the successful operation.</param>
	/// <param name="expected">The labels returned by <see cref="FromDescriptor"/> / <see cref="FromOperations"/>.</param>
	public static IReadOnlyList<FlowLabel> MissingLabels(DescribeProcessResult described,
			IReadOnlyList<FlowLabel> expected) {
		if (expected.Count == 0 || described?.Flows is null) {
			return Array.Empty<FlowLabel>();
		}

		List<FlowLabel> missing = [];
		foreach (FlowLabel wanted in expected) {
			DescribedFlow? flow = FindFlow(described, wanted);
			if (flow is null) {
				continue;
			}

			string drawn = (flow.Label ?? string.Empty).Trim();
			if (wanted.Label.Length == 0) {
				// A clear: only a read-back that still shows text is evidence. An empty read-back is the
				// requested state AND what a dropped field looks like, so it is not a finding.
				if (drawn.Length != 0) {
					missing.Add(wanted);
				}
				continue;
			}

			if (!string.Equals(drawn, wanted.Label, StringComparison.Ordinal)) {
				missing.Add(wanted);
			}
		}

		return missing;
	}

	/// <summary>
	/// The caller-facing warning for labels that did not land. Returns <c>null</c> when nothing is missing, so
	/// a caller can treat null as "no warning to emit".
	/// </summary>
	/// <param name="missing">The labels returned by <see cref="MissingLabels"/>.</param>
	public static string? BuildWarning(IReadOnlyList<FlowLabel> missing) {
		if (missing.Count == 0) {
			return null;
		}

		string subject = missing.Count == 1 ? "flow" : "flows";
		return $"The operation reported success, but the read-back shows no diagram label on the {subject} "
			+ $"{Describe(missing)}. The usual cause is a deployed CrtProcessBuilder below "
			+ $"{MinimumPackageVersion}, which declares no 'label' field on a flow and therefore discards it "
			+ "silently. Update the package (clio install-process-builder) and re-apply the labels, or label "
			+ "the connectors in the process designer. Until then a decision with two branches renders as two "
			+ "identical unlabelled arrows.";
	}

	/// <summary>
	/// The warning for labels whose landing could NOT be checked, because the read-back itself was
	/// unavailable.
	/// <para>It exists for the same reason the access-rights guard has one, and the argument is stronger here:
	/// the decision NOT to raise the package floor for this field rests entirely on the read-back being able
	/// to report a drop. With the read-back gone, a labels-only payload would otherwise print a plain success
	/// on an environment that discarded every label — and the intent-based unverified warning says nothing,
	/// because a labels-only payload configures no block.</para>
	/// </summary>
	/// <param name="expected">The labels the payload asked for.</param>
	/// <param name="reason">Why the description could not be read.</param>
	public static string? BuildUnverifiedWarning(IReadOnlyList<FlowLabel> expected, string reason) {
		if (expected.Count == 0) {
			return null;
		}

		string subject = expected.Count == 1 ? "flow" : "flows";
		return $"Could not verify that the diagram label on the {subject} {Describe(expected)} landed: "
			+ $"{reason}. The operation itself succeeded. A CrtProcessBuilder below {MinimumPackageVersion} "
			+ "discards the field silently, and this read-back is the only signal that it did — re-read the "
			+ "process with describe-business-process before reporting the labels as applied.";
	}

	// One rendering for both warnings, so the way a flow is named cannot drift between them.
	private static string Describe(IReadOnlyList<FlowLabel> flows) {
		List<string> pairs = [];
		foreach (FlowLabel flow in flows) {
			pairs.Add($"{Sanitize(flow.Source)} -> {Sanitize(flow.Target)} ('{Sanitize(flow.Label)}')");
		}

		return string.Join(", ", pairs);
	}

	/// <summary>
	/// Caller-supplied text made safe to echo into a warning: line breaks collapsed to spaces and the length
	/// capped, with an ellipsis when it was cut.
	/// <para>The label is the first field on this path where multi-line prose is the INTENDED input, and this
	/// warning is read by an agent as tool-result text. A label carrying a line break followed by
	/// something that looks like a log prefix would otherwise forge what reads as a separate line inside
	/// clio's own output, and an unbounded one would bloat it. The package applies the same two rules to every
	/// value it interpolates into a message (<c>SafeText.Sanitize</c>) and states the same threat model; this
	/// is the clio-side half of it.</para>
	/// <para>It bounds the MESSAGE only. Nothing bounds what is STORED, deliberately - see the remark on
	/// <c>ProcessGraphBuilder.ApplyLabel</c>: a flow caption is excluded from code generation and
	/// <c>SysLocalizableValue.Value</c> is <c>nvarchar(MAX)</c>, so there is nothing to protect there and a
	/// write-level bound would only differ from what an element caption has always allowed.</para>
	/// </summary>
	private static string Sanitize(string text) {
		if (string.IsNullOrEmpty(text)) {
			return text;
		}

		string oneLine = text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Trim();
		return oneLine.Length <= MaxRenderedTextLength
			? oneLine
			: oneLine.Substring(0, MaxRenderedTextLength) + "…";
	}

	// One collector for both payload shapes: a build descriptor's flows[] entries and the modify operations
	// that write a flow carry the same three fields at the same level, so splitting this in two would be two
	// implementations of one rule. Only the op filter and the supersede rule differ, and both are arguments.
	private static IReadOnlyList<FlowLabel> CollectLabelled(JsonArray entries, bool filterByOp) {
		// KEYED BY ENDPOINT PAIR, LAST WRITE WINS. Operations apply in order, so when a batch labels the same
		// flow twice only the last label is the one the read-back can express — keeping the earlier one would
		// report it as dropped on an edit that applied exactly as asked, which is the false positive this
		// guard's whole design is meant to avoid. The insertion order is preserved so the warning lists the
		// flows in the order the caller wrote them.
		Dictionary<(string Source, string Target), FlowLabel> byEndpoints =
			new(EndpointPairComparer.Instance);
		List<(string Source, string Target)> order = [];
		foreach (JsonNode? entry in entries) {
			if (entry is not JsonObject candidate) {
				continue;
			}

			if (filterByOp && !IsLabelWritingOperation(candidate[OpKey])) {
				continue;
			}

			string? label = ReadText(candidate[LabelKey]);
			string? source = ReadText(candidate[SourceKey]);
			string? target = ReadText(candidate[TargetKey]);
			// A flow with no endpoints is not addressable in the read-back at all, and a MISSING label member
			// asked for nothing. A blank one is kept: it is the deliberate clear, and MissingLabels verifies it
			// asymmetrically.
			if (label == null
					|| string.IsNullOrWhiteSpace(source)
					|| string.IsNullOrWhiteSpace(target)) {
				continue;
			}

			// TRIMMED, because both server write paths trim before resolving an element (the build path trims
			// the descriptor's source/target, and FindFlowNode trims a modify operation's), and describe
			// reports the canonical element name. Storing the padded form here would make FindFlow miss and
			// the guard verify nothing at all — a false NEGATIVE on exactly the payload it exists to catch.
			FlowLabel flow = new(source!.Trim(), target!.Trim(), label!.Trim());
			(string, string) key = (flow.Source, flow.Target);
			if (!byEndpoints.ContainsKey(key)) {
				order.Add(key);
			}
			byEndpoints[key] = flow;
		}

		List<FlowLabel> labelled = [];
		foreach ((string, string) key in order) {
			labelled.Add(byEndpoints[key]);
		}

		return labelled;
	}

	private static bool IsLabelWritingOperation(JsonNode? node) {
		string? op = ReadText(node)?.Trim();
		if (string.IsNullOrEmpty(op)) {
			return false;
		}

		foreach (string writer in LabelWritingOperations) {
			if (string.Equals(op, writer, StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
		}

		return false;
	}

	// Endpoint names are compared case-insensitively, matching how the server resolves an element by name.
	private static DescribedFlow? FindFlow(DescribeProcessResult described, FlowLabel wanted) {
		foreach (DescribedFlow flow in described.Flows) {
			if (string.Equals(flow?.Source, wanted.Source, StringComparison.OrdinalIgnoreCase)
					&& string.Equals(flow?.Target, wanted.Target, StringComparison.OrdinalIgnoreCase)) {
				return flow;
			}
		}

		return null;
	}

	private static string? ReadText(JsonNode? node) => BlockExpectationJson.ReadText(node);

	// The supersede key has to agree with FindFlow, which matches endpoints case-insensitively: with an
	// ordinal key, `setFlow` on "decide"->"Yes" would not supersede an `addFlow` on "Decide"->"Yes" and the
	// earlier label would be reported as dropped.
	private sealed class EndpointPairComparer : IEqualityComparer<(string Source, string Target)> {
		internal static readonly EndpointPairComparer Instance = new();

		public bool Equals((string Source, string Target) left, (string Source, string Target) right) =>
			string.Equals(left.Source, right.Source, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(left.Target, right.Target, StringComparison.OrdinalIgnoreCase);

		public int GetHashCode((string Source, string Target) pair) =>
			HashCode.Combine(
				StringComparer.OrdinalIgnoreCase.GetHashCode(pair.Source),
				StringComparer.OrdinalIgnoreCase.GetHashCode(pair.Target));
	}
}
