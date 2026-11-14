using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio;
using Clio.Command;
using Clio.Command.McpServer;
using Clio.Command.McpServer.Tools;
using Clio.UserEnvironment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Pins where a host reads what a tool does: a resident tool's <c>_meta</c> in <c>tools/list</c>, and
/// <c>get-tool-contract</c> with <c>detail=facts</c> for every tool, <c>clio-run</c>'s long tail included. Also pins
/// that the agent-facing index and contracts do not change.
/// </summary>
[TestFixture]
[Property("Module", "McpServer")]
public sealed class McpToolFactsSurfaceTests {

	private static McpToolInvokerRegistry BuildRegistry() {
		IServiceProvider provider = Substitute.For<IServiceProvider>();
		IFeatureToggleService featureToggle = Substitute.For<IFeatureToggleService>();
		featureToggle.IsEnabled(Arg.Any<Type>()).Returns(true);
		return new McpToolInvokerRegistry(
			provider, typeof(McpFeatureToggleFilter).Assembly, featureToggle, JsonSerializerOptions.Default);
	}

	private static ISettingsRepository SettingsRepositoryStub() {
		ISettingsRepository repository = Substitute.For<ISettingsRepository>();
		repository.GetEnvironment(Arg.Any<string>()).Returns(new EnvironmentSettings());
		return repository;
	}

	[Test]
	[Category("Unit")]
	[Description("The published facts carry the four fields by name and, for argument values that narrow them, " +
		"by-argument entries with every field resolved and when-absent only where it is set.")]
	public void ToJson_ShouldPublishTheFourFieldsAndTheResolvedArgumentEntries() {
		// Arrange
		McpToolFacts facts = new(
			McpToolEffect.Destroy,
			McpToolEnvironmentImpact.Rebuild,
			McpToolScope.Environment,
			McpToolConnections.None,
			[
				new McpToolArgumentFacts("action", "list", false, McpToolEffect.Read,
					McpToolEnvironmentImpact.None, McpToolScope.Environment, McpToolConnections.None),
				new McpToolArgumentFacts("dry-run", "true", true, McpToolEffect.Read,
					McpToolEnvironmentImpact.None, McpToolScope.Environment, McpToolConnections.None)
			]);

		// Act
		string json = facts.ToJson().ToJsonString();

		// Assert
		json.Should().Be(
			"{\"effect\":\"destroy\",\"rebuilds-environment\":true,\"scope\":\"environment\",\"connections\":\"none\","
			+ "\"by-argument\":["
			+ "{\"argument\":\"action\",\"value\":\"list\",\"effect\":\"read\",\"rebuilds-environment\":false,\"scope\":\"environment\",\"connections\":\"none\"},"
			+ "{\"argument\":\"dry-run\",\"value\":\"true\",\"when-absent\":true,\"effect\":\"read\",\"rebuilds-environment\":false,\"scope\":\"environment\",\"connections\":\"none\"}]}",
			because: "a host parses this shape, so its field names and values are the contract");
	}

	[Test]
	[Category("Unit")]
	[Description("A catalog argument entry that leaves a field out takes the tool's own value, so a host reads every " +
		"field of every published entry without merging; a field the tool leaves out reads back as Unspecified.")]
	public void Parse_ShouldResolveAnArgumentEntrysMissingFieldsFromTheTool() {
		// Arrange
		const string catalog = """
			{
			  "zz-tool": {"effect": "destroy", "rebuilds-environment": true, "scope": "machine", "connections": "read",
			    "by-argument": [{"argument": "dry-run", "value": "true", "effect": "read", "rebuilds-environment": false}]},
			  "zz-partial": {"effect": "read"}
			}
			""";

		// Act
		IReadOnlyDictionary<string, McpToolFacts> facts = McpToolFactsReader.Parse(catalog);

		// Assert
		McpToolArgumentFacts entry = facts["zz-tool"].ByArgument.Should().ContainSingle(
			because: "the entry declares one argument value").Which;
		entry.Effect.Should().Be(McpToolEffect.Read, because: "the entry declares its own effect");
		entry.EnvironmentImpact.Should().Be(McpToolEnvironmentImpact.None, because: "the entry declares its own impact");
		entry.Scope.Should().Be(McpToolScope.Machine, because: "a scope the entry leaves out takes the tool's");
		entry.Connections.Should().Be(McpToolConnections.Read, because: "a connections fact the entry leaves out takes the tool's");
		facts["ZZ-PARTIAL"].UnspecifiedFieldNames.Should().BeEquivalentTo(
			[nameof(McpToolFacts.EnvironmentImpact), nameof(McpToolFacts.Scope), nameof(McpToolFacts.Connections)],
			because: "a field the catalog leaves out must surface for the coverage test, and names match case-insensitively");
	}

	[Test]
	[Category("Unit")]
	[Description("Every tool in tools/list carries its declared facts in _meta under clio/facts, set once when the " +
		"server options are built.")]
	public void ToolsList_ShouldCarryEachResidentToolsFactsInMeta() {
		// Arrange
		IServiceCollection services = new ServiceCollection();
		BindingsModule.RegisterMcpServer(services, SettingsRepositoryStub());
		using ServiceProvider provider = services.BuildServiceProvider();

		// Act
		McpServerOptions options = provider.GetRequiredService<IOptions<McpServerOptions>>().Value;
		McpServerTool[] listed = [.. options.ToolCollection!];

		// Assert
		listed.Should().NotBeEmpty(because: "the resident tools are what tools/list serves");
		listed.Select(tool => tool.ProtocolTool.Name).Should().Contain(["clio-run", "get-tool-contract"],
			because: "the executor and the contract tool are always resident");
		foreach (McpServerTool tool in listed) {
			McpToolFactsReader.TryGet(tool.ProtocolTool.Name, out McpToolFacts declared).Should().BeTrue(
				because: $"'{tool.ProtocolTool.Name}' is served, so it must declare its facts");
			JsonNode? published = tool.ProtocolTool.Meta?[McpToolFacts.MetaKey];
			published.Should().NotBeNull(because: $"'{tool.ProtocolTool.Name}' must publish its facts in _meta");
			published!.ToJsonString().Should().Be(declared.ToJson().ToJsonString(),
				because: "tools/list must publish exactly what the tool declares");
		}
	}

	[Test]
	[Category("Unit")]
	[Description("detail=facts without tool-names answers, under facts, every tool the index lists plus " +
		"get-tool-contract and each deprecated alias, each with its declared facts.")]
	public void GetToolContract_ShouldAnswerEveryServedToolsFacts_WhenDetailIsFacts() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistry();
		ToolContractGetResponse index = ToolContractCatalog.GetContracts(
			null, registry, detail: ToolContractCatalog.IndexEntriesDetail);
		KeyValuePair<string, IReadOnlyList<string>> aliased = McpToolCompatibilityCatalog.SeedAliasesByCanonical.First();

		// Act
		ToolContractGetResponse response = ToolContractCatalog.GetContracts(
			null, registry, detail: ToolContractCatalog.FactsDetail);

		// Assert
		response.Success.Should().BeTrue(because: "a facts request over the served tools succeeds");
		response.Facts.Should().NotBeNull(because: "detail=facts answers under facts");
		response.Index.Should().BeNull(because: "a facts answer does not carry the index");
		response.ToolIndex.Should().BeNull(because: "a facts answer does not carry the line index");
		response.Facts!.Keys.Should().Contain(index.Index!.Select(entry => entry.Name),
			because: "a host must find every tool clio-run can reach");
		response.Facts.Keys.Should().Contain(ToolContractGetTool.ToolName,
			because: "get-tool-contract is a tool too, though the index leaves it out");
		response.Facts.Keys.Should().Contain(aliased.Value[0],
			because: "a call may carry a deprecated name, and the host must find its facts under it");
		response.Facts[aliased.Value[0]].ToJsonString().Should().Be(response.Facts[aliased.Key].ToJsonString(),
			because: "an alias runs its canonical tool");
		McpToolFactsReader.TryGet("manage-role", out McpToolFacts manageRole).Should().BeTrue(
			because: "manage-role declares its facts");
		response.Facts["manage-role"].ToJsonString().Should().Be(manageRole.ToJson().ToJsonString(),
			because: "the answer is the declaration");
	}

	[Test]
	[Category("Unit")]
	[Description("detail=facts with tool-names answers the named tools only; manage-role says list only reads; an " +
		"unknown name comes back in not-found.")]
	public void GetToolContract_ShouldAnswerTheNamedToolsFactsAndReportMisses_WhenDetailIsFactsWithNames() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistry();

		// Act
		ToolContractGetResponse response = ToolContractCatalog.GetContracts(
			["manage-role", "zz-no-such-tool"], registry, detail: ToolContractCatalog.FactsDetail);

		// Assert
		response.Success.Should().BeTrue(because: "one of the two names resolves");
		response.Facts!.Keys.Should().BeEquivalentTo(["manage-role"], because: "only the named, served tools are answered");
		response.NotFound.Should().ContainSingle(miss => miss.Name == "zz-no-such-tool",
			because: "a name that is no tool is reported like a contract lookup reports it");
		JsonArray byArgument = response.Facts["manage-role"]["by-argument"]!.AsArray();
		byArgument.Should().Contain(entry => (string)entry!["argument"]! == "action"
				&& (string)entry["value"]! == "list" && (string)entry["effect"]! == "read",
			because: "listing roles only reads, which is what the host needs to stop asking for a change approval");
	}

	[Test]
	[Category("Unit")]
	[Description("The agent-facing answers do not change: the line index and the index-entries objects carry no facts, " +
		"so their byte budgets and the shape ClioRing parses stay as they were.")]
	public void GetToolContract_ShouldLeaveTheIndexWithoutFacts_WhenDetailIsNotFacts() {
		// Arrange
		McpToolInvokerRegistry registry = BuildRegistry();

		// Act
		string lines = JsonSerializer.Serialize(ToolContractCatalog.GetContracts(null, registry));
		string entries = JsonSerializer.Serialize(ToolContractCatalog.GetContracts(
			null, registry, detail: ToolContractCatalog.IndexEntriesDetail));
		string named = JsonSerializer.Serialize(ToolContractCatalog.GetContracts(["list-apps"], registry));

		// Assert
		lines.Should().NotContain("\"facts\"", because: "the line index is paid on every discovery call");
		entries.Should().NotContain("\"facts\"", because: "ClioRing parses index-entries and its budget is pinned");
		named.Should().NotContain("\"facts\"", because: "a named contract's size is budgeted per fetch");
	}
}
