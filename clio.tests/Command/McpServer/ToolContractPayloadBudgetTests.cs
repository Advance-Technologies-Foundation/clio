using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Byte/character ratchets over the <c>get-tool-contract</c> payloads, the discovery and contract
/// surface for every NON-resident tool.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="McpProfileGatingTests"/> ratchets <c>tools/list</c>, the payload every session pays for.
/// It says nothing about <c>get-tool-contract</c>, which is what a long-tail tool actually costs: the
/// compact index on discovery, then one tool's full contract on demand. That surface had no bound at
/// all, and ENG-96389 measured the consequence — the four ProcessDesigner descriptions grew roughly
/// 2.3x in twelve days with nothing to notice. (A character total is deliberately not quoted here: it
/// moves with every unrelated edit to any of the four and would be stale before the next reader.)
/// </para>
/// <para>
/// These ratchets do not shrink anything. They make growth a decision defended at review — raising a
/// ceiling here is the moment to ask whether the text belongs in a <c>[Description]</c> at all — rather
/// than an accident nobody measures. The ceilings follow the <see cref="McpProfileGatingTests"/>
/// convention when one has to move: re-pin to the MEASURED size rounded up to the next 256 bytes, never
/// to the next whole kilobyte, so the ratchet keeps ratcheting instead of handing the surface silent
/// room. All of them measure the DEFAULT surface (every feature-gated type off, matching a fresh install),
/// for the same reason that fixture does.
/// </para>
/// <para>
/// ONE deliberate deviation, called out here so the two constants below are not silently inconsistent
/// under identical-looking arithmetic: <c>MaxCompactIndexSerializedBytes</c> takes a larger slack than
/// the next-256 step. The convention was written for <c>tools/list</c>, a curated ~20-tool resident set
/// that changes rarely, and it is right there. The compact index instead covers every one of the ~130
/// long-tail tools and grows by TOOL COUNT — at the next-256 step it would have 93 bytes of headroom,
/// so the very next tool anyone adds fails it, and a ratchet that fires on every routine addition stops
/// being read and starts being bumped. It is pinned at roughly three tools of slack instead, which is
/// the smallest amount that still distinguishes sustained catalog growth from one ordinary addition.
/// <c>MaxToolContractDescriptionChars</c> follows the convention unchanged: prose growth is exactly
/// what a tight ceiling should catch.
/// </para>
/// </remarks>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class ToolContractPayloadBudgetTests {

	// Compact-index ceiling. The index is the ONE get-tool-contract payload with a fixed size: every
	// registered tool contributes a name, a <=120-char purpose and its safety/availability flags, whether
	// or not the agent ever calls it, so this ceiling grows by TOOL COUNT (roughly 180-200 bytes each)
	// rather than by description bulk. Measured 43660 bytes on the DEFAULT surface at 14e2dd5a9 plus
	// this branch's round-2 fixes — already 32% above the entire 33024-byte tools/list budget, which is
	// worth knowing but is NOT this ratchet's business to fix: ENG-96389 §3 measured that relocating
	// description content into guidance articles is token-NEGATIVE beyond 1.3 articles, so the index is
	// pinned where it stands.
	// Registration and Classic parameter-page discovery add two independent long-tail tools.
	// The combined default index measures 44986 bytes; round to the next 256-byte step (45056).
	// Re-pinned deliberately for issue #1221: odata-read-to-file is one more long-tail tool, and one more
	// tool is exactly what this ceiling is defined to grow by. Measured 45223 bytes on the default surface
	// with it registered - 237 bytes for its index entry, which is one entry's worth and nothing else: the
	// index carries only the FIRST SENTENCE of a description (BuildPurpose), so the sentences this branch
	// adds to odata-read's own [Description] cost the index nothing. Next 256-byte step is 45312 (177).
	// Re-pinned for ENG-90576: localize-page is one more long-tail tool. Measured 45442 bytes on the
	// default surface with it registered (130 bytes for its index entry); next 256-byte step is 45568 (178).
	// Re-pinned for ENG-101592: execute-esq-to-file, get-component-info-to-file, get-request-info-to-file and
	// list-entity-client-schemas-to-file are four more long-tail tools. Measured 46392 bytes on the default
	// surface with them registered (824 bytes, about 206 per index entry); next 256-byte step is 46592 (182).
	// ENG-101352 adds create-package, one more long-tail tool: measured 46567 bytes (175 for its index
	// entry), still inside the 46592-byte step, so the ceiling does not move.
	// Re-pinned for ENG-94638: get-mobile-page-conversion-guide went GA, so the converter is no longer
	// gated off the default surface and its index entry is now paid by every discovery call. Measured
	// 46788 bytes with it ungated and carrying a curated contract (221 for its index entry, which includes
	// contract-available flipping to true and the curated purpose replacing the reflected one); next
	// 256-byte step is 46848 (183). The tool stays long-tail on purpose - it is NOT in
	// McpCoreToolProfile.CoreToolTypes - so that entry is the whole per-session cost of the un-gate.
	// Re-pinned for ENG-99741: set-object-rights and get-object-rights are two more long-tail tools. Measured 47257
	// bytes on the default surface with them registered as well (469 bytes for the two); next 256-byte step is
	// 47360 (185).
	// Re-pinned for ENG-103017: place-section-in-workplace is one more long-tail tool with a curated contract.
	// Measured 47495 bytes with it registered (238 for its index entry); next 256-byte step is 47616 (186).
	// Re-pinned for ENG-103024: trigger-smoke is one more long-tail tool on top of that (about 190 for its index
	// entry); next 256-byte step is 47872 (187).
	// Serialization uses the default JSON encoder,
	// which escapes non-ASCII (a purpose ellipsis is written as a 6-byte escape), so it over-counts the real
	// UTF-8 wire size — conservative, which is the safe direction for a ceiling.
	// Since ENG-102796 this ceiling covers the OBJECT index, which a no-names call returns only for
	// detail=index-entries (ClioRing asks for it); the default reply is the line index below.
	private const int MaxCompactIndexSerializedBytes = 187 * 256;

	// Line-index ceiling (ENG-102796). The default no-names reply renders each index entry as ONE line,
	// "name (R)(D) [aka ...]: purpose" with the purpose cut to 48 characters, plus a legend - because the object
	// index above is ~47 KB and Copilot CLI spills any tool result past ~20 KB to a file the agent then reads
	// back in pieces, which it did 15 times in one CAADT release build. Unlike the object index, this ceiling
	// has a hard upper bound: MaxInlineWireReplyBytes, the inline limit below. It follows the next-256 convention rather than the object index's
	// three-tools-of-slack deviation: a new tool already re-pins that ceiling, so this one moving with it adds no
	// review, and every re-pin is then a moment to read the distance left to that limit. Measured 17636
	// bytes for 228 tools on the default surface (about 75 bytes a line; 216 of the 228 purposes are cut); next
	// 256-byte step is 17664 (69). The same reply is 16871 bytes on the wire, where the MCP result encoder writes
	// the ellipsis and the legend's quotes unescaped - so the spill line is about 30 tools away for an agent
	// (2129 bytes at ~72 a line) and about 18 for this conservative measure.
	//
	// Re-pinned to 70 * 256 = 17920 before the first release, by a legend sentence rather than a tool: the review
	// found that "(D) = destructive" read as "a line without (D) is read-only", which install-gate and reg-web-app
	// (destructive=false, both write) contradict, so the legend now says a line without (D) can still write.
	// Measured 17675 for the same 228 tools; the reply on the wire stays about 2 KB under the spill line.
	private const int MaxToolIndexSerializedBytes = 70 * 256;

	// Worst-case ceiling for ONE named full contract, measured as the SERIALIZED contract in UTF-8 bytes
	// — the same quantity the index ratchet above measures, and what the agent actually receives.
	// Measuring `description` alone would leave an escape hatch open: argument descriptions are free-form
	// multi-line prose copied into the contract by McpToolRegistrySchemaContract.BuildInputSchema, so the
	// cheapest way to pass a description-only ceiling after the next element block is to move the text
	// into an argument description or an example. The per-fetch cost would be unchanged, the ratchet
	// green, and the budget decision this test exists to force skipped (ENG-96389 review).
	//
	// This is the number ENG-96389 watched double: create-business-process answers a single
	// get-tool-contract call with more than the ENTIRE tools/list budget, for one tool. Pinning the
	// MAXIMUM rather than the sum keeps the guard on what ONE fetch costs, which is what an agent pays.
	// Measured 34748 bytes (create-business-process) after ENG-99856 named subProcess.multiInstanceOptions
	// inline, with modify-business-process at 34712 and create-entity-business-rules third at 32010;
	// 136 * 256 = 34816 per the next-256 convention, re-pinned from 134 when CrtProcessBuilder 1.6.2.18
	// added the activity-result selection: modify-business-process gained the setFlowResults operation and
	// create-business-process the flows[].results field, and the Approval paragraph in the latter had to be
	// rewritten because it instructed the dialect that produces an unmaintainable branch.
	//
	// The failure message asks whether the text belongs in a [Description] at all, and that question was
	// answered rather than waved past: the first cut measured 35674, and roughly a kilobyte of it - the
	// designer mechanics, why a formula there is invisible, what a human sees - moved to the guidance,
	// where it now lives in process-activity-result-branches: that article was split out of
	// process-branch-conditions later, and the text followed the rule rather than the file. What stayed inline is what a caller
	// must decide at CALL time: which of the two predicate slots this connector takes, that they are
	// mutually exclusive, and what each refusal is. That is the split ENG-96389 section 5 identified as the
	// one that pays - cutting depth WITHIN a block, not relocating the block.
	//
	// ENG-99856 spent its 68 bytes on a SWAP rather than an addition, which is the shape that fits here: the
	// version appositive said which archive the floor names - pure provenance, nothing a caller decides at
	// call time - and it was replaced by the member they must actually write,
	// subProcess.multiInstanceOptions {enabled, executionMode, ignoreErrors}. A manual run on a live stand
	// is why: an agent reached for `subProcess.multiInstance`, one word off, because this contract named no
	// member and the guidance that does is published on a separate train. Net +3 bytes on create, -2 on
	// modify.
	//
	// Be honest about what 68 bytes of headroom means rather than claiming a wording fix passes: the
	// default JSON encoder escapes every non-ASCII character and apostrophe as a six-byte unicode
	// escape, and these descriptions are dense with both, so the room is roughly THREE escaped
	// characters — not a clause, not a word. Adding anything at all to create-business-process trips
	// this, and modify-business-process is 10 bytes behind it, so the same is true of both.
	// These numbers are re-measured on every cut that touches either description, by lowering the
	// ceiling and reading the failure message, because a stale figure here overstates the slack and the
	// next author finds a red ratchet where the comment promised room. The ENG-92707 round-3 numbers
	// replaced 34165 / 139 / 33334, which were three cuts old. That tightness is
	// deliberate on the two tools this ticket is about, and the failure message names the largest three
	// so an author who edited a different one is not sent to the wrong file. If it starts firing on
	// edits that are NOT budget decisions, re-pin it deliberately and say so here — never widen it in
	// passing.
	//
	// ENG-92711 (the Script task element) paid for its text the same way, by SWAP: create-business-process
	// dropped the floor-history paragraph about 1.4.0.58/.60 and the 88%/65% condition statistics - provenance,
	// nothing decided at call time - and spent the room on the scriptTask block, usings[], and the compile
	// signal becoming conditional; modify-business-process shortened the same history and lost a
	// parenthetical. The process methods (`methods`, `setMethods`) followed the same way. Re-measured by
	// lowering the ceiling: modify-business-process 34765, create-business-process 34731,
	// describe-business-process 32900 (which gained the scriptTask block, usings[] and methods). Both write
	// tools are within 90 bytes of the ceiling, so the warning above holds for both.
	//
	// Re-pinned DELIBERATELY to 137 * 256 = 35072 when ENG-92711 met ENG-91844 on master (2026-09-30). Each
	// had paid for its text by swap and fit alone; together they measured modify-business-process 34883,
	// create-business-process 34863 and describe-business-process 33148 - up to 67 bytes over. What the two
	// added is what a caller writes (sourceColumn / elementParameter.column, the scriptTask block, usings[],
	// methods, the operations that edit them), and the provenance around them was already swapped out, so the
	// cut would have been a caller-facing fact. 189 bytes of headroom is about thirty escaped characters.
	//
	// Re-measured by lowering the ceiling after ENG-102113 (2026-10-05): modify-business-process 35053,
	// create-business-process 34863, describe-business-process 33148. modify paid for the schema-registry
	// clause - which value a Lookup on Add/Modify/Delete data's object holds, and that only setElement sets or
	// changes the object - by tightening its own wording, not by raising the ceiling; that leaves it 19 bytes,
	// about three escaped characters.
	//
	// ENG-102114 (2026-10-06) changed what a modify condition accepts - [#Name#] forms are expanded as on
	// create, and a hand-written meta path must have every segment dot-separated - and paid for the
	// setFlowCondition rewrite by dropping the refusal-message detail process-formulas already owns:
	// modify-business-process 35047, create-business-process 34656.
	//
	// ENG-102112 (2026-10-08) added the preconfiguredPage and openEditPage blocks to modify's addElement list
	// and the generic-route refusal to both descriptions. Modify paid by merging its four per-block entries,
	// which each repeated "same block as create-business-process" behind an escaped em dash, into one:
	// modify-business-process 35016, create-business-process 34854, describe-business-process 33367.
	//
	// ENG-99970 (the get-target-package routing sentence, "Take packageName from get-target-package.",
	// placed second so a shortened contract keeps it) adds about 42 bytes to create-business-process; under
	// the 137 * 256 ceiling that leaves it well inside, so the sentence needs no swap.
	//
	// ENG-102111 (2026-10-09, process tracing) added the setTracing operation to modify-business-process,
	// isTracing to create-business-process and the tracing block to describe-business-process, each naming
	// get-guidance name=process-tracing for the costs rather than carrying them. Modify paid by SWAP: the
	// sentence saying removeFlow's refusal ships from CrtProcessBuilder 1.6.0.10 and what happens below it was
	// dead once the floor reached 1.6.6.92, since clio refuses such an environment up front. Re-measured by
	// lowering the ceiling: modify-business-process 35048, create-business-process 35027,
	// describe-business-process 33463 - 24 and 45 bytes of headroom.
	private const int MaxToolContractSerializedBytes = 137 * 256;

	// The seven-process-contract bundle is measured as it goes ON THE WIRE - serialized with the MCP result
	// options (McpResultSize: McpResultJsonEncoder, nulls omitted) - against 19 000 bytes, the inline limit the
	// CAADT release gate is budgeted against. Copilot CLI returned results of up to ~19.8 KB inline and spilled
	// ~20.7 KB and over to a file in release build 16156272 and the ENG-99970 A/B on creatio_3 (October 2026); the
	// 21.3 KB that InlineReplyBudgetBytes cites is the smallest spill the earlier ENG-100154 transcripts happened
	// to contain, inside the same bracket. The bundle is NOT held to InlineReplyBudgetBytes: Fit measures with the
	// default encoder, which escapes for HTML and writes every null, so it overstates the wire size - by about
	// 1 KB here - and keeps fitting conservative. ENG-102797 spends that difference: the short create- and
	// modify-business-process contracts carry their call shapes, so Fit's measure of this bundle stays over
	// 18 432 even all short (pinned below) while the reply the agent receives stays inline.
	private const int MaxInlineWireReplyBytes = 19_000;

	[Test]
	[Category("Unit")]
	[Description("The compact discovery index returned by a no-arguments get-tool-contract call - one line per tool plus the legend - stays within its byte budget, which sits below the size at which an agent CLI spills a tool result to a file.")]
	public void GetToolContracts_ShouldKeepCompactIndexSerializedSizeWithinBudget_WhenCalledWithoutToolNames() {
		// Arrange
		ToolContractGetTool tool = BuildToolOverDefaultSurface();

		// Act
		ToolContractGetResponse response = tool.GetToolContracts();
		int payloadBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(response));

		// Assert
		response.ToolIndex.Should().NotBeNullOrEmpty(
			because: "a no-arguments call is the discovery entry point and must answer with the compact index");
		payloadBytes.Should().BeLessThanOrEqualTo(MaxToolIndexSerializedBytes,
			because: $"the line index is paid on every discovery call, and every session makes one; measured {payloadBytes} bytes against the {MaxToolIndexSerializedBytes}-byte ceiling. Past {MaxInlineWireReplyBytes} bytes it spills to a file in an agent CLI - shorten the purpose cut or the legend before raising this");
		MaxToolIndexSerializedBytes.Should().BeLessThanOrEqualTo(MaxInlineWireReplyBytes,
			because: "a ceiling raised past the spill line would let the index grow back into the read-back it was cut to remove");
	}

	[Test]
	[Category("Unit")]
	[Description("The object index returned for detail=index-entries - the shape ClioRing parses - stays within its byte budget, ratcheting the per-tool flags it carries.")]
	public void GetToolContracts_ShouldKeepIndexEntriesSerializedSizeWithinBudget_WhenDetailIsIndexEntries() {
		// Arrange
		ToolContractGetTool tool = BuildToolOverDefaultSurface();

		// Act
		ToolContractGetResponse response = tool.GetToolContracts(
			new ToolContractGetArgs(Detail: ToolContractCatalog.IndexEntriesDetail));
		int payloadBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(response));

		// Assert
		response.Index.Should().NotBeNullOrEmpty(
			because: "detail=index-entries must answer with the per-tool objects");
		payloadBytes.Should().BeLessThanOrEqualTo(MaxCompactIndexSerializedBytes,
			because: $"the object index grows by tool count like the line index does; measured {payloadBytes} bytes against the {MaxCompactIndexSerializedBytes}-byte ceiling");
	}

	[Test]
	[Category("Unit")]
	[Description("No single tool's FULL contract exceeds the per-fetch byte budget, ratcheting what the complete contract costs an agent that asks for it with detail=full.")]
	public void GetToolContracts_ShouldKeepLargestContractWithinBudget_WhenEveryIndexedToolIsNamed() {
		// Arrange
		// Every tool is resolved BY NAME, one call each, exactly as an agent reaches a long-tail tool, and
		// with detail=full: since ENG-100154 a default named lookup is FITTED to one inline reply, so without
		// it the largest contracts would be measured in their short form and this ratchet would pass while
		// measuring nothing. (A no-names detail=full is not the same request - it answers from
		// CanonicalToolNames, the CURATED catalog only, and would drop every uncurated tool, which is where
		// the unbounded [Description] attributes actually live.)
		ToolContractGetTool tool = BuildToolOverDefaultSurface();
		string[] toolNames = (tool.GetToolContracts(new ToolContractGetArgs(Detail: ToolContractCatalog.IndexEntriesDetail)).Index ?? [])
			.Select(entry => entry.Name)
			.ToArray();

		// Act
		(string Name, int Bytes)[] resolved = toolNames
			.Select(name => {
				ToolContractDefinition? contract = tool
					.GetToolContracts(new ToolContractGetArgs([name], ToolContractShortForm.FullDetail))
					.Tools?.SingleOrDefault();
				return (
					Name: name,
					Bytes: contract is null
						? 0
						: Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(contract)));
			})
			.ToArray();
		(string Name, int Bytes) largest = resolved.MaxBy(entry => entry.Bytes);

		// Assert
		toolNames.Should().NotBeEmpty(
			because: "the compact index enumerates every tool the ratchet has to cover");
		// GetToolContracts catches every exception and answers Success:false with Tools left null, so a
		// contract that blows up is measured as 0 characters and silently drops out of the ceiling below.
		// Without this guard the ratchet cannot tell "this tool's description is small" from "this tool
		// stopped being measured", and the headline case it exists for could vanish while the test stays
		// green. (get-tool-contract itself is absent from the index by design and so is not measured here.)
		resolved.Should().OnlyContain(entry => entry.Bytes > 0,
			because: "a name the compact index advertises whose contract does not resolve would be counted as zero and escape the ceiling");
		string leaderboard = string.Join(", ", resolved
			.OrderByDescending(entry => entry.Bytes)
			.Take(3)
			.Select(entry => $"{entry.Name} {entry.Bytes}B"));
		largest.Bytes.Should().BeLessThanOrEqualTo(MaxToolContractSerializedBytes,
			because: $"one get-tool-contract call must stay under {MaxToolContractSerializedBytes} bytes; largest three are {leaderboard}. If you did not edit the tool named first, check yours - this ceiling tracks the MAXIMUM, so any description crossing it fails here. Raising it is the moment to ask whether the text belongs in a [Description] at all");
	}

	[Test]
	[Category("Unit")]
	[Description("A default named lookup of ANY single tool fits one inline reply (ENG-100154), so reading a contract is one turn and never a spill to a file that the agent then greps back in pieces.")]
	public void GetToolContracts_ShouldFitEverySingleToolLookupInline_WhenDetailIsOmitted() {
		// Arrange
		ToolContractGetTool tool = BuildToolOverDefaultSurface();
		string[] toolNames = (tool.GetToolContracts(new ToolContractGetArgs(Detail: ToolContractCatalog.IndexEntriesDetail)).Index ?? []).Select(entry => entry.Name).ToArray();

		// Act
		(string Name, int Bytes, string? Detail)[] replies = toolNames
			.Select(name => {
				ToolContractGetResponse reply = tool.GetToolContracts(new ToolContractGetArgs([name]));
				return (Name: name, Bytes: Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(reply)),
					Detail: reply.Tools?.SingleOrDefault()?.Detail);
			})
			.ToArray();

		// Assert
		toolNames.Should().NotBeEmpty(because: "the compact index enumerates every tool this guard has to cover");
		replies.Where(reply => reply.Bytes > ToolContractShortForm.InlineReplyBudgetBytes)
			.Select(reply => $"{reply.Name} {reply.Bytes}B")
			.Should().BeEmpty(
				because: $"a single default lookup must fit {ToolContractShortForm.InlineReplyBudgetBytes} bytes; a tool "
					+ "listed here does not fit even in its short form, so its short form keeps too much");
		replies.Where(reply => reply.Detail is not null).Should().NotBeEmpty(
			because: "anti-vacuity: at least the process-designer contracts are far over the budget in full, so a "
				+ "run in which nothing came back short did not exercise the fitting at all");
	}

	[Test]
	[Category("Unit")]
	[Description("The request measured in the CAADT transcripts - the seven process-designer contracts in ONE call, 34 KB for one of them in full - fits one inline reply by default, measured as it goes on the wire (ENG-100154, ENG-102797).")]
	public void GetToolContracts_ShouldFitTheSevenProcessDesignerContractsInline_WhenRequestedTogether() {
		// Arrange
		ToolContractGetTool tool = BuildToolOverDefaultSurface();
		string[] processTools = [
			"create-business-process", "modify-business-process", "describe-business-process",
			"validate-process-graph", "list-user-tasks", "modify-business-process-as-new-version",
			"set-active-business-process-version"
		];

		// Act
		ToolContractGetResponse reply = tool.GetToolContracts(new ToolContractGetArgs(processTools));
		long replyBytes = McpResultSize.Of(reply);

		// Assert
		reply.Tools.Should().HaveCount(processTools.Length,
			because: "fitting shortens contracts, it never drops one the caller asked for");
		replyBytes.Should().BeLessThanOrEqualTo(MaxInlineWireReplyBytes,
			because: $"this exact request answered with well over 100 KB before ENG-100154 and was grepped back in "
				+ $"pieces through the shell; measured {replyBytes} bytes on the wire");
	}

	[Test]
	[Category("Unit")]
	[Description("The ENG-102797 trade, pinned: the default lookup of the seven process-designer contracts returns every one of them short - the floor, as it did before the call shapes - and, with both process-write shapes, Fit's own measure of that floor is over its budget, so Fit stops for want of anything left to cut rather than because the reply fits. A change that keeps one of the seven whole, or that brings the floor back under Fit's budget, fails here, so the trade is re-decided instead of drifting. The wire size of that reply is the guard above.")]
	public void GetToolContracts_ShouldReturnEverySevenProcessDesignerContractShort_WhenTheCallShapesOutgrowFitsBudget() {
		// Arrange
		ToolContractGetTool tool = BuildToolOverDefaultSurface();
		string[] processTools = [
			"create-business-process", "modify-business-process", "describe-business-process",
			"validate-process-graph", "list-user-tasks", "modify-business-process-as-new-version",
			"set-active-business-process-version"
		];

		// Act
		ToolContractGetResponse reply = tool.GetToolContracts(new ToolContractGetArgs(processTools));
		string[] shortened = [.. reply.Tools!
			.Where(contract => contract.Detail == ToolContractShortForm.ShortDetail)
			.Select(contract => contract.Name)];
		int fitMeasure = ToolContractShortForm.MeasureBytes(reply);

		// Assert
		shortened.Should().BeEquivalentTo(processTools,
			because: "Fit shortens the largest contract first and needs all seven short to get as close to its "
				+ "budget as it can; every short form keeps its safety duties, and a contract kept whole here would "
				+ "be a change in what this bundle costs on the wire");
		fitMeasure.Should().BeGreaterThan(ToolContractShortForm.InlineReplyBudgetBytes,
			because: $"Fit measures the all-short reply at {fitMeasure} bytes with the default encoder: the call "
				+ "shapes spend Fit's margin over the wire size. If this fails the bundle fits Fit's budget again, and "
				+ "the CallShapes remarks, InlineReplyBudgetBytes and the knowledge record about fitting must say so");
	}

	[Test]
	[Category("Unit")]
	[Description("describe-environment - the named contract ClioRing reads - comes back from a default lookup byte-identical to its full form, so fitting never changes what Ring receives.")]
	public void GetToolContracts_ShouldReturnDescribeEnvironmentUnchanged_ByDefault() {
		// Arrange
		ToolContractGetTool tool = BuildToolOverDefaultSurface();

		// Act
		ToolContractGetResponse byDefault = tool.GetToolContracts(new ToolContractGetArgs(["describe-environment"]));
		ToolContractGetResponse full = tool.GetToolContracts(
			new ToolContractGetArgs(["describe-environment"], ToolContractShortForm.FullDetail));

		// Assert
		byDefault.Tools!.Single().Detail.Should().BeNull(
			because: "ClioRing parses this contract and must never receive a short form of it");
		JsonSerializer.Serialize(byDefault).Should().Be(JsonSerializer.Serialize(full),
			because: "ClioRing compatibility rests on this contract being byte-identical with and without detail=full");
	}

	// Builds get-tool-contract over the REAL invoker registry so uncurated tools resolve through the same
	// registry-schema path clio-run dispatches against.
	//
	// NAMED for the surface it builds, deliberately. ToolContractGetToolTests declares a
	// BuildToolWithRegistry() in this same namespace whose registry enables EVERY tool type; this one is
	// its inverse. Two same-named helpers measuring opposite tool sets would invite a maintainer to
	// "consolidate the duplication" and silently change what these ratchets measure - folding the all-on
	// version in here blows the headroom below, and folding this one into the sibling fixture drops the
	// gated tools out of the uniqueness guard with nothing turning red.
	//
	// The predicate IS McpProfileGatingTests.DefaultSurfaceEnabled, called rather than re-implemented -
	// every [FeatureToggle] type OFF - so both fixtures measure the surface a FRESH INSTALL serves and
	// cannot drift apart. With every toggle on
	// instead, the five gated tool types (deploy-identity, uninstall-identity, create-oauth-technical-user,
	// watch-compilation, mobile-page-conversion-guide) add roughly 900-1000 bytes to the index: more than
	// the whole headroom above, so an experimental tool that ships disabled would consume budget for a
	// payload no user receives, and promoting one to default-on would not move the number at all.
	private static ToolContractGetTool BuildToolOverDefaultSurface() {
		IServiceProvider provider = Substitute.For<IServiceProvider>();
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		featureToggle.IsEnabled(Arg.Any<Type>())
			.Returns(call => McpProfileGatingTests.DefaultSurfaceEnabled(call.Arg<Type>()));
		McpToolInvokerRegistry registry = new(
			provider,
			typeof(SchemaSyncTool).Assembly,
			featureToggle,
			JsonSerializerOptions.Default);
		return new ToolContractGetTool(registry);
	}
}
