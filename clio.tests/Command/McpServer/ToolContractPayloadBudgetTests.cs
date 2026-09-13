using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
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
/// all, and ENG-96389 measured the consequence — the four ProcessDesigner descriptions grew from
/// 35 910 to 81 106 characters in twelve days (2.26x) with nothing to notice.
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
	// rather than by description bulk. Measured 43564 bytes on the DEFAULT surface with this branch
	// applied (43683 after this branch also split the two identical clear-redis purposes) — already 32% above the entire 33024-byte tools/list budget, which is worth knowing but is
	// NOT this ratchet's business to fix: ENG-96389 §3 measured that relocating description content into
	// guidance articles is token-NEGATIVE beyond 1.3 articles, so the index is pinned where it stands.
	// 173 * 256 = 44288 leaves 605 bytes, about three tools - see the deviation note in the fixture remarks.
	// Serialization uses the default JSON encoder,
	// which escapes non-ASCII (a purpose ellipsis is written as a 6-byte escape), so it over-counts the real
	// UTF-8 wire size — conservative, which is the safe direction for a ceiling.
	private const int MaxCompactIndexSerializedBytes = 173 * 256;

	// Worst-case ceiling for ONE named full contract, measured in characters of the resolved
	// `description` field alone (the input schema and examples are bounded by the tool's argument
	// count; the description is the unbounded part). This is the number ENG-96389 watched double:
	// create-business-process answers a single get-tool-contract call with 31052 characters — one tool's
	// description, alone, is larger than the ENTIRE tools/list budget. Pinning the MAXIMUM rather than
	// the sum keeps the guard on what one fetch costs an agent, which is what it actually pays.
	// 122 * 256 = 31232 leaves 180 characters of headroom: a wording fix passes, a new element block
	// does not — and a new element block IS the budget decision this ratchet exists to force.
	private const int MaxToolContractDescriptionChars = 122 * 256;

	[Test]
	[Category("Unit")]
	[Description("The compact discovery index returned by a no-arguments get-tool-contract call stays within its byte budget, ratcheting the cost every long-tail discovery call pays.")]
	public void GetToolContracts_ShouldKeepCompactIndexSerializedSizeWithinBudget_WhenCalledWithoutToolNames() {
		// Arrange
		ToolContractGetTool tool = BuildToolWithRegistry();

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
	[Description("No single tool's resolved contract description exceeds the per-fetch character budget, ratcheting what one get-tool-contract call costs an agent.")]
	public void GetToolContracts_ShouldKeepLargestContractDescriptionWithinBudget_WhenEveryIndexedToolIsNamed() {
		// Arrange
		// Every tool is resolved BY NAME, one call each, exactly as an agent reaches a long-tail tool.
		// detail=full is deliberately NOT used: it answers from CanonicalToolNames, i.e. the CURATED
		// catalog only, so every uncurated tool - which is where the unbounded [Description] attributes
		// actually live - is absent from that payload and would escape the ratchet entirely.
		ToolContractGetTool tool = BuildToolWithRegistry();
		string[] toolNames = (tool.GetToolContracts().Index ?? [])
			.Select(entry => entry.Name)
			.ToArray();

		// Act
		(string Name, int Length)[] resolved = toolNames
			.Select(name => (
				Name: name,
				Length: tool.GetToolContracts(new ToolContractGetArgs([name]))
					.Tools?.SingleOrDefault()?.Description?.Length ?? 0))
			.OrderByDescending(entry => entry.Length)
			.ToArray();
		(string Name, int Length) largest = resolved.FirstOrDefault();

		// Assert
		toolNames.Should().NotBeEmpty(
			because: "the compact index enumerates every tool the ratchet has to cover");
		// GetToolContracts catches every exception and answers Success:false with Tools left null, so a
		// contract that blows up is measured as 0 characters and silently drops out of the ceiling below.
		// Without this guard the ratchet cannot tell "this tool's description is small" from "this tool
		// stopped being measured", and the headline case it exists for could vanish while the test stays
		// green. (get-tool-contract itself is absent from the index by design and so is not measured here.)
		resolved.Should().OnlyContain(entry => entry.Length > 0,
			because: "a name the compact index advertises whose contract does not resolve would be counted as zero and escape the ceiling");
		largest.Length.Should().BeLessThanOrEqualTo(MaxToolContractDescriptionChars,
			because: $"'{largest.Name}' answers one get-tool-contract call with {largest.Length} characters against the {MaxToolContractDescriptionChars}-character ceiling; raising it is the moment to ask whether the text belongs in a [Description] at all");
	}

	// Builds get-tool-contract over the REAL invoker registry so uncurated tools resolve through the same
	// registry-schema path clio-run dispatches against.
	//
	// The feature predicate matches McpProfileGatingTests' DefaultSurfaceEnabled - every [FeatureToggle]
	// type OFF - so both ratchets measure the surface a FRESH INSTALL serves. With every toggle on
	// instead, the five gated tool types (deploy-identity, uninstall-identity, create-oauth-technical-user,
	// watch-compilation, mobile-page-conversion-guide) add roughly 900-1000 bytes to the index: more than
	// the whole headroom above, so an experimental tool that ships disabled would consume budget for a
	// payload no user receives, and promoting one to default-on would not move the number at all.
	private static ToolContractGetTool BuildToolWithRegistry() {
		IServiceProvider provider = Substitute.For<IServiceProvider>();
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		featureToggle.IsEnabled(Arg.Any<Type>())
			.Returns(call => call.Arg<Type>().GetCustomAttribute<FeatureToggleAttribute>() is null);
		McpToolInvokerRegistry registry = new(
			provider,
			typeof(SchemaSyncTool).Assembly,
			featureToggle,
			JsonSerializerOptions.Default);
		return new ToolContractGetTool(registry);
	}
}
