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
/// than an accident nobody measures. Both ceilings follow the <see cref="McpProfileGatingTests"/>
/// convention when one has to move: re-pin to the MEASURED size rounded up to the next 256 bytes, never
/// to the next whole kilobyte, so the ratchet keeps ratcheting instead of handing the surface silent
/// room. Both also measure the DEFAULT surface (every feature-gated type off, matching a fresh install),
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
	// 173 * 256 = 44288 leaves 605 bytes, about three tools - see the deviation note in the fixture remarks.
	// Serialization uses the default JSON encoder,
	// which escapes non-ASCII (a purpose ellipsis is written as a 6-byte escape), so it over-counts the real
	// UTF-8 wire size — conservative, which is the safe direction for a ceiling.
	private const int MaxCompactIndexSerializedBytes = 173 * 256;

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
	// Measured 34165 bytes (create-business-process) at 14e2dd5a9 plus this branch's round-2 fixes;
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
	// Be honest about what 139 bytes of headroom means rather than claiming a wording fix passes: the
	// default JSON encoder escapes every non-ASCII character and apostrophe as a six-byte unicode
	// escape, and these descriptions are dense with both, so the room is roughly TWENTY escaped
	// characters — less than one clause. Adding a sentence to create-business-process trips this, and so
	// does growing modify-business-process, which sits 831 bytes behind at 33334. That tightness is
	// deliberate on the two tools this ticket is about, and the failure message names the largest three
	// so an author who edited a different one is not sent to the wrong file. If it starts firing on
	// edits that are NOT budget decisions, re-pin it deliberately and say so here — never widen it in
	// passing.
	private const int MaxToolContractSerializedBytes = 136 * 256;

	[Test]
	[Category("Unit")]
	[Description("The compact discovery index returned by a no-arguments get-tool-contract call stays within its byte budget, ratcheting the cost every long-tail discovery call pays.")]
	public void GetToolContracts_ShouldKeepCompactIndexSerializedSizeWithinBudget_WhenCalledWithoutToolNames() {
		// Arrange
		ToolContractGetTool tool = BuildToolOverDefaultSurface();

		// Act
		ToolContractGetResponse response = tool.GetToolContracts();
		int payloadBytes = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(response));

		// Assert
		response.Index.Should().NotBeNullOrEmpty(
			because: "a no-arguments call is the discovery entry point and must answer with the compact index");
		payloadBytes.Should().BeLessThanOrEqualTo(MaxCompactIndexSerializedBytes,
			because: $"the compact index is paid on every long-tail discovery call; measured {payloadBytes} bytes against the {MaxCompactIndexSerializedBytes}-byte ceiling");
	}

	[Test]
	[Category("Unit")]
	[Description("No single tool's resolved contract exceeds the per-fetch byte budget, ratcheting what one get-tool-contract call costs an agent.")]
	public void GetToolContracts_ShouldKeepLargestContractWithinBudget_WhenEveryIndexedToolIsNamed() {
		// Arrange
		// Every tool is resolved BY NAME, one call each, exactly as an agent reaches a long-tail tool.
		// detail=full is deliberately NOT used: it answers from CanonicalToolNames, i.e. the CURATED
		// catalog only, so every uncurated tool - which is where the unbounded [Description] attributes
		// actually live - is absent from that payload and would escape the ratchet entirely.
		ToolContractGetTool tool = BuildToolOverDefaultSurface();
		string[] toolNames = (tool.GetToolContracts().Index ?? [])
			.Select(entry => entry.Name)
			.ToArray();

		// Act
		(string Name, int Bytes)[] resolved = toolNames
			.Select(name => {
				ToolContractDefinition? contract = tool
					.GetToolContracts(new ToolContractGetArgs([name])).Tools?.SingleOrDefault();
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
