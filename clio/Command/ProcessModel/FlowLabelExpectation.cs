using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Post-operation check that a flow <c>label</c> the caller SENT was actually APPLIED by the server.
/// <para>The failure is silent, and silent in the ordinary direction rather than a corner: a
/// <c>CrtProcessBuilder</c> below 1.6.0.8 declares no <c>label</c> member on its flow descriptor, so its
/// <c>DataContractJsonSerializer</c> DISCARDS the field and the operation still answers <c>success:true</c>.
/// The caller gets the two identical unlabelled arrows the label exists to prevent, while every signal they
/// can see says the write worked.</para>
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

	/// <summary>One flow the caller asked to label, addressed the only way the write side allows.</summary>
	/// <param name="Source">Source element name, exactly as the caller wrote it.</param>
	/// <param name="Target">Target element name, exactly as the caller wrote it.</param>
	/// <param name="Label">The label text that was sent. Never empty — an empty one is a CLEAR, not a label.</param>
	public sealed record FlowLabel(string Source, string Target, string Label);

	/// <summary>
	/// The labels a BUILD descriptor asks for — every <c>flows[]</c> entry carrying a non-empty <c>label</c>.
	/// Returns an empty list when nothing was labelled, which skips the verification entirely.
	/// <para>An EMPTY label is deliberately not collected. It means "clear this label", and its success
	/// condition is the absence of a label, which is also what an old server that dropped the field leaves
	/// behind — so a read-back cannot tell the two apart and a warning here would be a guess.</para>
	/// </summary>
	/// <param name="descriptorJson">The build descriptor JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<FlowLabel> FromDescriptor(string descriptorJson) {
		JsonObject? descriptor = BlockExpectationJson.Parse(descriptorJson) as JsonObject;
		return descriptor?[FlowsKey] is JsonArray flows
			? CollectLabelled(flows)
			: Array.Empty<FlowLabel>();
	}

	/// <summary>
	/// The labels a MODIFY operations array asks for. Both write routes carry the field on the operation
	/// itself — <c>addFlow</c> and <c>setFlow</c> each take <c>source</c>, <c>target</c> and <c>label</c> — so
	/// unlike the email block there is no nested descriptor to reach into.
	/// </summary>
	/// <param name="operationsJson">The operations array JSON exactly as the caller supplied it.</param>
	public static IReadOnlyList<FlowLabel> FromOperations(string operationsJson) =>
		BlockExpectationJson.Parse(operationsJson) is JsonArray operations
			? CollectLabelled(operations)
			: Array.Empty<FlowLabel>();

	/// <summary>
	/// Of the labels that were sent, those the read-back shows are NOT on the flow.
	/// <para>A flow the description does not contain is NOT reported. The endpoint pair may have been written
	/// differently (a UId rather than a name), or the flow may have been removed later in the same batch, and
	/// neither is evidence that a label was dropped — the guard's whole value is that it does not cry wolf on
	/// a build that worked.</para>
	/// <para>A label that came back DIFFERENT is reported too, not just an absent one. The server trims what
	/// it stores, so the comparison ignores surrounding whitespace; anything else is a real disagreement
	/// between what was asked for and what is drawn.</para>
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

			if (!string.Equals((flow.Label ?? string.Empty).Trim(), wanted.Label.Trim(), StringComparison.Ordinal)) {
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

		List<string> pairs = [];
		foreach (FlowLabel flow in missing) {
			pairs.Add($"{flow.Source} -> {flow.Target} ('{flow.Label}')");
		}

		string subject = missing.Count == 1 ? "flow" : "flows";
		return $"The operation reported success, but the read-back shows no diagram label on the {subject} "
			+ $"{string.Join(", ", pairs)}. The usual cause is a deployed CrtProcessBuilder below 1.6.0.8, which "
			+ "declares no 'label' field on a flow and therefore discards it silently. Update the package "
			+ "(clio install-process-builder) and re-apply the labels, or label the connectors in the process "
			+ "designer. Until then a decision with two branches renders as two identical unlabelled arrows.";
	}

	// One collector for both payload shapes: a build descriptor's flows[] entries and the modify
	// operations that write a flow carry the same three fields at the same level, so splitting this in two
	// would be two implementations of one rule.
	private static IReadOnlyList<FlowLabel> CollectLabelled(JsonArray entries) {
		List<FlowLabel> labelled = [];
		foreach (JsonNode? entry in entries) {
			if (entry is not JsonObject candidate) {
				continue;
			}

			string? label = ReadText(candidate[LabelKey]);
			string? source = ReadText(candidate[SourceKey]);
			string? target = ReadText(candidate[TargetKey]);
			// A blank label is a CLEAR and is unverifiable; a flow with no endpoints is not addressable in the
			// read-back at all.
			if (string.IsNullOrWhiteSpace(label)
					|| string.IsNullOrWhiteSpace(source)
					|| string.IsNullOrWhiteSpace(target)) {
				continue;
			}

			labelled.Add(new FlowLabel(source!, target!, label!));
		}

		return labelled;
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

	private static string? ReadText(JsonNode? node) =>
		node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
