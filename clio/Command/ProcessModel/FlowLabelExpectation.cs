using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Clio.Common;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Post-operation check that a flow <c>label</c> the caller SENT was actually APPLIED by the server.
/// <para>The failure is silent, and silent in the ordinary direction rather than a corner: a
/// <c>CrtProcessBuilder</c> that PREDATES the flow <c>label</c> declares no <c>label</c> member on its
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

	// There is deliberately NO minimum-version constant here, and no version number in any text this
	// class renders. A number cannot BE the diagnosis. Both write commands and describe carry
	// [RequiresPackage] for this package, and RequiredPackageChecker throws on a convergence refusal for
	// every environment whose installed version is below the one clio SHIPS - so a caller who can reach
	// this guard at all is already above any floor a message could name, and "update to X.Y.Z.W" is
	// advice they have already satisfied: a dead end that hides the real cause instead of naming it.
	//
	// The reachable cause is a CAPABILITY gap, not an ordering one - a cut from a line that never carried
	// the member, which docs/agent-instructions/bundled-packages.md records under "a numeric floor cannot
	// express a CAPABILITY once two branches cut numbers". So the three sibling guards' convention holds
	// here too: name the cause qualitatively, the way EmailBlockExpectation says "predates the sendEmail
	// element", and point at clio install-process-builder.

	// How many FLOWS a single warning will name before it says "+ N more". The server accepts 1 000
	// operations per request, and every one of them can carry a label, so an unbounded join renders a
	// thousand endpoint pairs into an agent's context - defeating the per-value cap below, which bounds
	// each label and not their number. Ten is enough to act on and short enough to read.
	private const int MaxRenderedFlows = 10;

	// How much caller-supplied text a warning will echo. Long enough that a real label is never cut - the
	// shipped corpus's longest is well under it - and short enough that a pathological one cannot bloat the
	// message an agent reads.
	private const int MaxRenderedTextLength = 200;

	/// <summary>The modify operations that WRITE a flow label. Nothing else on the operations array does.</summary>
	private static readonly string[] LabelWritingOperations = ["addFlow", "setFlow"];

	/// <summary>
	/// The operation that makes a pending expectation MOOT rather than superseding it.
	/// <para>Not a label writer, and not ignorable either. <c>addFlow(A,B,"Yes")</c> then
	/// <c>removeFlow(A,B)</c> then a plain <c>addFlow(A,B)</c> leaves a flow with no label and an
	/// expectation that says "Yes" — the remove was filtered out by op, and the re-add carries no label so
	/// it does not supersede. The guard then reports a dropped label and tells the caller to install a
	/// package, on a batch that did exactly what was asked: the precise false positive this design exists
	/// to avoid.</para>
	/// </summary>
	private const string FlowForgettingOperation = "removeFlow";

	/// <summary>One flow the caller asked to label, addressed the only way the write side allows.</summary>
	/// <param name="Source">Source element name, trimmed the way the server trims it before resolving it.</param>
	/// <param name="Target">Target element name, trimmed the same way.</param>
	/// <param name="Label">
	/// The label text that was sent, trimmed. An EMPTY string is a deliberate CLEAR and is carried as one:
	/// see <see cref="Missing"/> for the asymmetry in how the two are verified.
	/// </param>
	public sealed record FlowLabel(string Source, string Target, string Label);

	/// <summary>
	/// One label that did not land, paired with what the read-back actually shows on that flow.
	/// <para><see cref="Drawn"/> is the datum that separates the three outcomes <see cref="Missing"/>
	/// reports, and the warning is false for two of them without it: it is the only thing distinguishing "the
	/// package discarded the field" (nothing drawn) from "the server stored something else" (something else
	/// drawn), and the only way a caller can see that a CLEAR left the old text standing.</para>
	/// </summary>
	/// <param name="Wanted">The label the caller asked for. An empty <c>Label</c> was a request to CLEAR.</param>
	/// <param name="Drawn">What the read-back reports on that flow, trimmed; empty when it carries none.</param>
	public sealed record FlowLabelMiss(FlowLabel Wanted, string Drawn);

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
	public static IReadOnlyList<FlowLabelMiss> Missing(DescribeProcessResult described,
			IReadOnlyList<FlowLabel> expected) {
		if (expected.Count == 0 || described?.Flows is null) {
			return Array.Empty<FlowLabelMiss>();
		}

		List<FlowLabelMiss> missing = [];
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
					missing.Add(new FlowLabelMiss(wanted, drawn));
				}
				continue;
			}

			if (!string.Equals(drawn, wanted.Label, StringComparison.Ordinal)) {
				missing.Add(new FlowLabelMiss(wanted, drawn));
			}
		}

		return missing;
	}

	/// <summary>
	/// Of the labels that were sent, those this guard can NEVER verify, because the caller addressed the
	/// flow by UId rather than by element name.
	/// <para>Both write paths accept either form — <c>FindFlowNode</c> tries a UId first — while
	/// <c>describe</c> reports <c>source</c> and <c>target</c> as element NAMES. So a UId-addressed write
	/// produces an expectation that <see cref="Missing"/> can never match, and a dropped label on it goes
	/// unreported with no caveat either: the unverified warning fires only when the describe itself failed,
	/// and this describe succeeds.</para>
	/// <para>That matters more than an ordinary blind spot, because the read-back is the SOLE reason this
	/// field has no raised <c>[RequiresPackage]</c> floor. Silence here is the exact state the floor was not
	/// raised on the strength of avoiding.</para>
	/// <para>Detected by GUID SHAPE **and** by the read-back, and it needs both. Shape alone is not enough:
	/// on the BUILD path a UId cannot address anything - the descriptor resolves endpoints through a
	/// name-keyed dictionary that fails on a miss - so a GUID-shaped endpoint that survives a build belongs
	/// to an element legitimately NAMED like one, which describe reports and <see cref="FindFlow"/> matches.
	/// Without the read-back conjunct every such build printed "there is nothing to compare" beside a
	/// comparison that had just succeeded.</para>
	/// <para>And "the flow was not found" alone is not enough either, in the other direction: an unfound
	/// NAME-addressed flow is genuinely ambiguous - it may have been removed later in the same batch - and
	/// reporting all of those would cry wolf on working builds, which is what <see cref="Missing"/> exists
	/// not to do. It takes the conjunction to name only the flows that could never have been checked.</para>
	/// <para>One case it still reports and arguably should not: a label addressed by UId whose flow was then
	/// REMOVED later in the same batch. The remove is keyed on the endpoint pair as a STRING, so a
	/// name-addressed <c>removeFlow</c> cannot forget a UId-addressed expectation, and the two states —
	/// "removed, so moot" and "never verifiable" — are the same bytes from here. Separating them needs a
	/// UId-to-name resolution clio does not have. It is noise rather than a wrong claim: the label genuinely
	/// was not verified.</para>
	/// </summary>
	/// <param name="described">The description read back after the successful operation.</param>
	/// <param name="expected">The labels returned by <see cref="FromDescriptor"/> / <see cref="FromOperations"/>.</param>
	public static IReadOnlyList<FlowLabel> UidAddressed(DescribeProcessResult described,
			IReadOnlyList<FlowLabel> expected) {
		List<FlowLabel> uidAddressed = [];
		foreach (FlowLabel wanted in expected) {
			// Guid.TryParse rather than TryParseExact("D"): the server's FindFlowNode accepts whatever
			// Guid.Parse accepts, so the N/B/P/X forms address a flow just as well and a narrower net would
			// stay silent on exactly the payloads this exists for. An element NAMED as 32 bare hex digits is
			// the theoretical cost, and the read-back conjunct below covers it anyway.
			bool addressedByUid = Guid.TryParse(wanted.Source, out _) || Guid.TryParse(wanted.Target, out _);
			if (!addressedByUid) {
				continue;
			}
			
			// A description carrying NO flows at all is reported, not skipped, and that is the answer rather
			// than a convenience: with nothing to compare against, a UId-addressed label is unverifiable for
			// the same reason it always is, only more so. <see cref="Missing"/> returns empty in that state
			// because an absent flow is not evidence of a DROP; here the claim is weaker and survives it.
			// The null check is also required: Flows is a plain nullable list and this file's own dominant
			// test idiom leaves it unset, so dereferencing it turns a successful operation into an NRE on
			// its warning path.
			if (described?.Flows is null || FindFlow(described, wanted) is null) {
				uidAddressed.Add(wanted);
			}
		}

		return uidAddressed;
	}

	/// <summary>
	/// The caveat for a label addressed by UId, which no read-back can confirm.
	/// </summary>
	/// <param name="uidAddressed">The labels returned by <see cref="UidAddressed"/>.</param>
	public static string? BuildUidAddressedWarning(IReadOnlyList<FlowLabel> uidAddressed) {
		if (uidAddressed.Count == 0) {
			return null;
		}

		string subject = Subject(uidAddressed.Count);
		return $"The operation reported success, but the diagram label on the {subject} "
			+ $"{Describe(uidAddressed)} could NOT be verified: the flow was addressed by UId, and the "
			+ "read-back reports a flow's endpoints as element NAMES, so there is nothing to compare. A "
			+ "CrtProcessBuilder that PREDATES the flow label discards it silently, and this read-back "
			+ "is the only signal that it did. Re-send the same operation naming the SOURCE and TARGET "
			+ "elements, or re-read the process with describe-business-process, before reporting the label "
			+ "as applied.";
	}

	/// <summary>
	/// The caller-facing warning for labels that did not land. Returns <c>null</c> when nothing is missing, so
	/// a caller can treat null as "no warning to emit".
	/// <para><see cref="Missing"/> reports THREE different outcomes and they need three different
	/// sentences, because two of them make the obvious wording false. A label that came back DIFFERENT is not
	/// "no label", and a package that discards the field cannot be the cause — one that discards it leaves
	/// nothing, not something else. A CLEAR that did not land leaves the OLD text drawn, so telling the caller
	/// to re-apply a label they asked to remove inverts what they wanted. Only the absent case is caused by an
	/// old package, so only it prescribes <c>install-process-builder</c> — which is a destructive tool that
	/// runs a configuration build and restarts the instance, and must not be recommended for a cause it cannot
	/// fix.</para>
	/// <para>Every case shows what IS drawn, because that is the datum a caller needs to tell an old label
	/// that survived from a server that stored something else.</para>
	/// </summary>
	/// <param name="missing">The misses returned by <see cref="Missing"/>.</param>
	public static string? BuildWarning(IReadOnlyList<FlowLabelMiss> missing) {
		if (missing.Count == 0) {
			return null;
		}

		List<FlowLabelMiss> absent = [];
		List<FlowLabelMiss> different = [];
		List<FlowLabelMiss> notCleared = [];
		// Classified on DRAWN first, not on what was wanted. Missing never emits a miss with both halves
		// empty - it skips that case - but FlowLabelMiss is public, so a hand-built one can reach here,
		// and testing Wanted first rendered it as "(still drawn: '') still carries a diagram label".
		// Asking "is anything drawn?" first makes every branch true of what it describes whatever it is
		// handed.
		foreach (FlowLabelMiss miss in missing) {
			if (miss.Drawn.Length == 0) {
				absent.Add(miss);
			} else if (miss.Wanted.Label.Length == 0) {
				notCleared.Add(miss);
			} else {
				different.Add(miss);
			}
		}

		List<string> parts = [];
		if (absent.Count != 0) {
			parts.Add($"The read-back shows no diagram label on the {Subject(absent.Count)} "
				+ $"{DescribeWanted(absent)}. The usual cause is a deployed CrtProcessBuilder that PREDATES "
				+ "the flow label: it declares no 'label' field on a flow and therefore discards it "
				+ "silently. This is USUALLY a CAPABILITY gap rather than an old version number: clio normally "
				+ "refuses an environment running a package older than the one it ships, so what is missing is "
				+ "the member rather than a higher number. Reinstall the package clio ships "
				+ "(clio install-process-builder) and "
				+ "re-apply the labels, or "
				+ "label the connectors in the process designer. Until then a decision with two branches "
				+ "renders as two identical unlabelled arrows.");
		}

		if (different.Count != 0) {
			parts.Add($"The read-back shows DIFFERENT text on the {Subject(different.Count)} "
				+ $"{DescribeDifference(different)}. The package version is NOT the cause here - one that "
				+ "discards the field leaves nothing rather than something else - so the label reached the "
				+ "server and came back changed: the process was edited elsewhere between the write and this "
				+ "read, or the text was normalised on the way in. Re-read it with describe-business-process "
				+ "and re-apply only if the drawn text is not what the diagram should say.");
		}

		if (notCleared.Count != 0) {
			parts.Add($"The {Subject(notCleared.Count)} {DescribeDrawn(notCleared)} still carries a diagram "
				+ "label after being asked to drop it, so the connector still reads the old text. Re-apply the "
				+ "clear, or remove the label in the process designer. Updating the package will not help: one "
				+ "that discards the field leaves the old label exactly where it was.");
		}

		return "The operation reported success. " + string.Join(" ", parts);
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

		return $"Could not verify that the diagram label on the {Subject(expected.Count)} "
			+ $"{Describe(expected)} landed: "
			+ $"{reason}. The operation itself succeeded. A CrtProcessBuilder that PREDATES the flow label "
			+ "discards the field silently, and this read-back is the only signal that it did — re-read the "
			+ "process with describe-business-process before reporting the labels as applied.";
	}

	/// <summary>
	/// The label text as the SERVER will store it, so the read-back can be compared like for like.
	/// <para>Trimmed, because both write paths trim. And filtered for the characters an XML
	/// resource attribute cannot hold, because the package filters them at its own write funnel - a
	/// caption is extracted into the schema resource file and written there as an attribute value. Without
	/// this, a label of <c>"Ap[U+0001]proved"</c> stores as <c>Approved</c>, the expectation still holds the
	/// unfiltered text, and <see cref="Missing"/> reports a DIFFERENT-text miss on a write that did exactly
	/// what the package documents.</para>
	/// <para>Tab, LF and CR are KEPT, matching the package: they are legal XML and the writer escapes
	/// them. A label made ENTIRELY of unstorable characters is not this method's problem - the package
	/// REFUSES that outright, so the operation fails and no read-back comparison happens.</para>
	/// </summary>
	private static string AsStored(string label) {
		var kept = new StringBuilder(label.Length);
		int index = 0;
		while (index < label.Length) {
			char character = label[index];
			// A surrogate PAIR is one valid astral character and survives intact; a LONE half is not a
			// character and the server drops it, so a per-char filter cannot express this.
			if (char.IsHighSurrogate(character)
				&& index + 1 < label.Length
				&& char.IsLowSurrogate(label[index + 1])) {
				kept.Append(character).Append(label[index + 1]);
				index += 2;
				continue;
			}

			// Advanced BEFORE the branches below, so the counter is never touched inside them. A `for` here
			// updated its own stop-condition variable in the body (Sonar S127) - the pair branch has to
			// consume TWO code units, which a for-header cannot express.
			index++;
			if (char.IsSurrogate(character)) {
				// A PAIRED high surrogate was consumed above, so anything reaching here is an unpaired half:
				// not a character at all.
				continue;
			}
			if (IsUnstorable(character)) {
				continue;
			}

			kept.Append(character);
		}

		return kept.ToString().Trim();
	}

	// Mirrors ProcessGraphBuilder.StripXmlInvalid / IsXmlInvalid on the package side, INCLUDING the
	// surrogate handling above and U+FFFE / U+FFFF here. A hand mirror across two repositories, like the
	// DataMember names. What keeps the two from drifting is a human reading both halves plus the paired
	// tests on each side - and ONE mechanical check: the bundled-package fixture greps the shipped archive
	// for this rule's own source text, beside the DataMember probes. That probe is not decoration. The SHA
	// pin cannot do this job and says so itself - it detects a CHANGED archive, not a STALE one - so
	// without the content probe an archive cut before a widening satisfies every pin here while clio
	// predicts a stored label the server never stores. That mis-prediction surfaces as "the text came back
	// DIFFERENT" quoting two strings that look identical, because the character at fault is invisible. The first version of both halves tested char.IsControl alone, which a
	// Copilot review caught: those code points are not control characters and are not valid XML either,
	// so a label carrying one passed the filter and still broke resource serialization on the server.
	private static bool IsUnstorable(char character) =>
		(char.IsControl(character) && character != '\t' && character != '\n' && character != '\r')
			|| character == '\uFFFE' || character == '\uFFFF';

	private static string Subject(int count) => count == 1 ? "flow" : "flows";

	// One join for every renderer, so the cap cannot be applied to some warnings and not others.
	private static string Join<T>(IReadOnlyList<T> items, Func<T, string> render) {
		IEnumerable<string> rendered = items.Take(MaxRenderedFlows).Select(render);
		string listed = string.Join(", ", rendered);
		int hidden = items.Count - MaxRenderedFlows;
		return hidden > 0 ? $"{listed} (+ {hidden} more)" : listed;
	}

	// One rendering of the endpoint pair for every warning, so the way a flow is NAMED cannot drift between
	// them. What is quoted after the pair differs per outcome, which is the point of having four of these.
	private static string Endpoints(FlowLabel flow) =>
		$"{Sanitize(flow.Source)} -> {Sanitize(flow.Target)}";

	private static string Describe(IReadOnlyList<FlowLabel> flows) =>
		Join(flows, flow => $"{Endpoints(flow)} ('{Sanitize(flow.Label)}')");

	// The absent case: the asked-for label is the useful one to echo, because nothing is drawn - which
	// is the same rendering the unverified/uid-addressed warnings use, so it delegates rather than
	// producing a byte-identical string from a second place.
	private static string DescribeWanted(IReadOnlyList<FlowLabelMiss> misses) =>
		Describe(misses.Select(miss => miss.Wanted).ToList());

	// The mismatch case needs BOTH, and needs them distinguishable: which one is drawn is the whole finding.
	private static string DescribeDifference(IReadOnlyList<FlowLabelMiss> misses) =>
		Join(misses, miss =>
			$"{Endpoints(miss.Wanted)} (asked for '{Sanitize(miss.Wanted.Label)}', drawn "
			+ $"'{Sanitize(miss.Drawn)}')");

	// The failed clear: the caller asked for '' and echoing that tells them nothing. What is still on the
	// connector is the finding.
	private static string DescribeDrawn(IReadOnlyList<FlowLabelMiss> misses) =>
		Join(misses, miss => $"{Endpoints(miss.Wanted)} (still drawn: '{Sanitize(miss.Drawn)}')");

	/// <summary>
	/// Caller-supplied text made safe to echo into a warning: every control character replaced by a space and
	/// the length capped, with an ellipsis when it was cut.
	/// <para>The label is the first field on this path where multi-line prose is the INTENDED input, and this
	/// warning is read by an agent as tool-result text. A label carrying a line break followed by
	/// something that looks like a log prefix would otherwise forge what reads as a separate line inside
	/// clio's own output, and an unbounded one would bloat it. The package applies the same two rules to every
	/// value it interpolates into a message (<c>SafeText.Sanitize</c>) and states the same threat model; this
	/// is the clio-side half of it, and it is the shared <see cref="TextUtilities.SanitizeForDisplay"/> rather
	/// than a local rule, so the two cannot drift.</para>
	/// <para>It bounds the MESSAGE only. What is STORED is bounded differently and for a different reason:
	/// the write path filters what an XML attribute cannot hold - not only CONTROL characters, but also
	/// U+FFFE, U+FFFF and a lone surrogate half (see <c>ProcessGraphBuilder.StripXmlInvalid</c>) - because a
	/// caption
	/// is extracted into an XML resource attribute, but it applies no LENGTH cap, since a flow caption is
	/// excluded from code generation and <c>SysLocalizableValue.Value</c> is <c>nvarchar(MAX)</c>.</para>
	/// </summary>
	private static string Sanitize(string text) =>
		// Delegates to the shared helper rather than re-implementing it. The local version collapsed only
		// CR/LF, which is NARROWER than the threat model stated above: U+001B and U+0085 (NEL) both forge
		// output in a terminal and both survived it. SanitizeForDisplay replaces EVERY control character,
		// which is the rule this doc claims. Its ellipsis is "..." rather than a single glyph; that is the
		// only behavioural difference, and one test asserts on it.
		TextUtilities.SanitizeForDisplay(text, MaxRenderedTextLength);

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

			if (filterByOp && ConsumedByANonWritingOperation(candidate, byEndpoints, order)) {
				continue;
			}

			FlowLabel? flow = ReadLabelledFlow(candidate);
			if (flow is null) {
				continue;
			}

			(string, string) key = (flow.Source, flow.Target);
			if (!byEndpoints.ContainsKey(key)) {
				order.Add(key);
			}
			byEndpoints[key] = flow;
		}

		return order.Select(key => byEndpoints[key]).ToList();
	}

	/// <summary>
	/// Handles an operation that does not WRITE a label, reporting whether it consumed the entry.
	/// <para>Two outcomes, and the caller treats them alike: a <c>removeFlow</c> is acted on (its pair is
	/// forgotten) and anything other than <c>addFlow</c> / <c>setFlow</c> is simply not a label write. Either
	/// way the entry contributes no expectation.</para>
	/// <para>Split out with <see cref="ReadLabelledFlow"/> for a measured reason rather than taste: with both
	/// inline the collector's cognitive complexity was 27, and extracting only the forget step left it at 17
	/// against a limit of 15 — Sonar S3776 twice. The op decision and the field parse are two separate
	/// judgements about one entry, and reading them as three named steps is also how the loop now says what
	/// it does: consume, parse, or record.</para>
	/// </summary>
	private static bool ConsumedByANonWritingOperation(JsonObject candidate,
			Dictionary<(string Source, string Target), FlowLabel> byEndpoints,
			List<(string Source, string Target)> order) {
		string? op = BlockExpectationJson.ReadText(candidate[OpKey])?.Trim();
		if (string.Equals(op, FlowForgettingOperation, StringComparison.OrdinalIgnoreCase)) {
			Forget(candidate, byEndpoints, order);
			return true;
		}

		return !IsLabelWritingOperation(candidate[OpKey]);
	}

	/// <summary>
	/// One entry read as a labelled flow, or <c>null</c> when it asks for no label this guard can verify.
	/// <para>A flow with no endpoints is not addressable in the read-back at all, and a MISSING label member
	/// asked for nothing. A BLANK one is kept: it is the deliberate clear, and <see cref="Missing"/> verifies
	/// it asymmetrically.</para>
	/// <para>Endpoints are TRIMMED, because both server write paths trim before resolving an element (the
	/// build path trims the descriptor's source/target, and <c>FindFlowNode</c> trims a modify operation's)
	/// and describe reports the canonical element name. Storing the padded form would make
	/// <see cref="FindFlow"/> miss and the guard verify nothing at all — a false NEGATIVE on exactly the
	/// payload it exists to catch.</para>
	/// </summary>
	private static FlowLabel? ReadLabelledFlow(JsonObject candidate) {
		string? label = BlockExpectationJson.ReadText(candidate[LabelKey]);
		string? source = BlockExpectationJson.ReadText(candidate[SourceKey]);
		string? target = BlockExpectationJson.ReadText(candidate[TargetKey]);
		if (label == null
				|| string.IsNullOrWhiteSpace(source)
				|| string.IsNullOrWhiteSpace(target)) {
			return null;
		}

		return new FlowLabel(source.Trim(), target.Trim(), AsStored(label));
	}

	/// <summary>
	/// Drops any pending expectation for the pair a <c>removeFlow</c> names.
	/// <para>Extracted from the loop rather than inlined. With it inline the collector reached a cognitive
	/// complexity of 27 against a limit of 15 - four levels of nesting for one bookkeeping step that has
	/// nothing to do with reading a label. Same behaviour, and the loop now reads as the three decisions it
	/// actually makes: forget, skip, or record.</para>
	/// </summary>
	private static void Forget(JsonObject candidate,
		Dictionary<(string Source, string Target), FlowLabel> byEndpoints,
		List<(string Source, string Target)> order) {
		string? source = BlockExpectationJson.ReadText(candidate[SourceKey]);
		string? target = BlockExpectationJson.ReadText(candidate[TargetKey]);
		if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)) {
			return;
		}

		(string, string) key = (source.Trim(), target.Trim());
		if (byEndpoints.Remove(key)) {
			order.RemoveAll(pair => EndpointPairComparer.Instance.Equals(pair, key));
		}
	}

	private static bool IsLabelWritingOperation(JsonNode? node) {
		string? op = BlockExpectationJson.ReadText(node)?.Trim();
		return !string.IsNullOrEmpty(op)
			&& LabelWritingOperations.Contains(op, StringComparer.OrdinalIgnoreCase);
	}

	// Endpoint names are compared case-insensitively, matching how the server resolves an element by name.
	//
	// AMBIGUITY RETURNS NULL rather than the first match. An endpoint pair is not unique on a
	// multigraph, and picking arbitrarily would compare one flow's label against another's and report
	// "the label did not land" on a write that applied exactly as sent - a FALSE POSITIVE in the only
	// signal this feature has. Null puts it in the same bucket as a flow that is not there at all:
	// unverified rather than wrongly accused, which is the right way round for a guard.
	//
	// The state is not reachable through this package today, and the check is here anyway because the
	// cost of being wrong is asymmetric. ProcessGraphBuilder.AddFlow refuses a second flow between an
	// already-connected pair, FindTheFlowBetween refuses to act when more than one matches (naming the
	// count), and the batch is atomic - a throw aborts before SaveEdited - so any label-writing
	// operation addressing a duplicated pair is refused before this guard ever runs. That is an
	// argument about the package clio SHIPS, and deliberately not one about every server: convergence
	// orders version NUMBERS and cannot promise a capability, which is the whole reason the warning
	// texts in this file stopped naming a floor. A designer-authored process can also hold such a
	// pair outright. So the reachability argument is why this is not urgent, and the asymmetric cost
	// of being wrong - a false accusation in the only signal this feature has - is why it is here.
	private static DescribedFlow? FindFlow(DescribeProcessResult described, FlowLabel wanted) {
		// Take(2) is the whole question: one match is the answer, two or more is ambiguity, and a third
		// tells us nothing further. The server accepts 1 000 operations per request and each can carry a
		// label, so an unbounded materialisation here is 1 000 full scans of a large designer process.
		List<DescribedFlow> matches = [.. described.Flows.Where(flow =>
			string.Equals(flow?.Source, wanted.Source, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(flow?.Target, wanted.Target, StringComparison.OrdinalIgnoreCase)).Take(2)];
		return matches.Count == 1 ? matches[0] : null;
	}

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
