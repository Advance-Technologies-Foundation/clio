using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Story 8 (ai-business-process-generation) end-to-end coverage for <c>describe-business-process</c>.
/// NOT in CI — run manually. The advertised-tool test is hermetic; the read test is gated on a
/// reachable environment with a known process (configured caption).
/// </summary>
/// <remarks>
/// The two version-family tests build the family they read - a process, one saved version, that version made
/// actual - so they depend on nothing a product ships. Every member stays on the stand: a version has no delete,
/// which is why the names are Guid-suffixed and why this belongs on a disposable sandbox.
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature(DescribeProcessTool.ToolName)]
[NonParallelizable]
[Category(McpE2ECategories.ProcessDesigner)]
public sealed class DescribeProcessToolE2ETests {

	private const string ToolName = DescribeProcessTool.ToolName;

	[Test]
	[Description("Starts the real clio MCP server and verifies describe-business-process is discoverable via the get-tool-contract compact index (hermetic).")]
	[AllureTag(ToolName)]
	[AllureName("describe-business-process is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and asserts the tool is reachable through the get-tool-contract compact index, without needing a stand.")]
	public async Task DescribeProcess_Should_Be_Advertised_By_Mcp_Server() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: false);

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: $"the {ToolName} MCP tool must be discoverable on the lazy surface (get-tool-contract compact index) even though it is not resident in tools/list");
	}

	[Test]
	[Description("Over the real MCP path, describe-business-process returns a structured graph for a known process.")]
	[AllureTag(ToolName)]
	[AllureName("describe-business-process returns a structured graph for a known process")]
	[AllureDescription("Calls the tool against the configured sandbox process and asserts the response is the structured element/flow graph rather than raw metadata.")]
	public async Task DescribeProcess_Should_ReturnStructuredGraph_ForKnownProcess() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = context.ProcessCode
		});

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a structured envelope should be returned, not a transport error");
		string callResultJson = JsonSerializer.Serialize(callResult);
		callResultJson.Should().Contain("elements",
			because: "describe-business-process returns the structured element graph, not raw metadata");
		callResultJson.Should().Contain("buildType",
			because: "each element carries its round-trippable buildType token — proof of real structured typing, not an echo");
		callResultJson.Should().Contain("flows",
			because: "the structured graph includes the sequence flows between elements");
	}

	[Test]
	[Description("Over the real MCP path, describe-business-process reports version 0, an active flag and a one-member root family for a process that has no versions.")]
	[AllureTag(ToolName)]
	[AllureName("describe-business-process reports an unversioned process as version 0")]
	[AllureDescription("Proves against a live stand that an unversioned process is reported as version 0 with a single root family member and NO warning, so absence of versions is a stated fact rather than an absent one.")]
	public async Task DescribeProcess_Should_ReportVersionZeroAndRootOnlyFamily_ForUnversionedProcess() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);

		// Act
		CallToolResult callResult = await AllureApi.Step($"Describe {context.ProcessCode} through MCP", () =>
			CallToolAsync(context, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = context.ProcessCode
			}));

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a structured envelope should be returned, not a transport error");
		JsonObject graph = ReadDescribedGraph(callResult);
		AllureApi.Step("Assert the version standing is established, not absent", () => {
			graph["version"]!.GetValue<int>().Should().Be(0,
				because: $"McpE2E:Sandbox:ProcessCode must name a process with no versions, and '{context.ProcessCode}' is read as version 0 in the process library");
			graph["isActiveVersion"]!.GetValue<bool>().Should().BeTrue(
				because: "the only member of a family is the version the runtime executes");
			graph.Should().NotContainKey("versionReadWarning",
				because: "the read succeeded, and the warning is what separates this answer from an unestablished one");
			graph["activeVersionSource"]!.GetValue<string>().Should().Be("process-library-view",
				because: "the answer names the authority that produced it, because the runtime consults another");
		});
		AllureApi.Step("Assert the family is the root alone", () => {
			JsonArray versions = graph["versions"]!.AsArray();
			versions.Should().HaveCount(1, because: "a process with no versions is a one-member family");
			versions[0]!["isRoot"]!.GetValue<bool>().Should().BeTrue(
				because: "that single member is the family root, which is the identity a future version hangs off");
			versions[0]!["name"]!.GetValue<string>().Should().Be(context.ProcessCode,
				because: "the family member describes the very schema that was read");
		});
	}

	[Test]
	[Description("Over the real MCP path, describing the ROOT of a version family by name reports the root as NOT the active version and names the version that runs. The test builds the family itself, so it needs no process a product ships.")]
	[AllureTag(ToolName)]
	[AllureName("describe-business-process names the active version of a family it builds")]
	[AllureDescription("Proves against a live stand that resolving a versioned process by name returns the family ROOT and says so: isActiveVersion is false, and the response names the version the runtime actually executes. The family is built by the test: a process, one version saved with modify-business-process-as-new-version, and that version made actual with set-active-business-process-version.")]
	public async Task DescribeProcess_Should_NameTheActiveVersion_WhenDescribingTheFamilyRootByName() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		VersionFamily family = await BuildFamilyWithActiveVersionAsync(context);

		// Act
		CallToolResult callResult = await AllureApi.Step($"Describe {family.RootName} through MCP", () =>
			CallToolAsync(context, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = family.RootName
			}));

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a structured envelope should be returned, not a transport error");
		JsonObject graph = ReadDescribedGraph(callResult);
		AllureApi.Step("Assert the read graph is not the one that runs", () => {
			graph["isActiveVersion"]!.GetValue<bool>().Should().BeFalse(
				because: $"'{family.RootName}' resolves to the family root, and the test made version 1 the one the runtime executes");
			graph["activeVersionName"]!.GetValue<string>().Should().Be(family.ActiveName,
				because: "the caller has to be handed the name of the running version, or it cannot correct itself");
			graph["versionRootSchemaUId"]!.GetValue<string>().Should().Be(graph["schemaUId"]!.GetValue<string>(),
				because: "the root of a family is its own family key, which is how the members were found");
		});
		AllureApi.Step("Assert the family is reported with both members", () => {
			graph["versions"]!.AsArray().Should().HaveCount(2,
				because: "the family is the root and the one version the test saved");
			graph["activeVersionSchemaUId"]!.GetValue<string>().Should()
				.NotBe(graph["schemaUId"]!.GetValue<string>(),
					because: "a version is a SEPARATE schema, so the running one cannot be the schema just read");
			graph.Should().NotContainKey("versionReadWarning",
				because: "these facts were established, and a warning beside them would contradict that");
		});
		AllureApi.Step("Assert every member names its package rather than only its UId", () => {
			foreach (JsonNode? member in graph["versions"]!.AsArray()) {
				JsonObject entry = member!.AsObject();
				string packageName = entry["packageName"]?.GetValue<string>();
				packageName.Should().NotBeNullOrWhiteSpace(
					because: $"'{entry["name"]!.GetValue<string>()}' lives in a package that exists on this "
						+ "stand, and a builder asking which one gets the answer read out of this field");
				Guid.TryParse(packageName, out _).Should().BeFalse(
					because: "the defect this guards rendered the answer as \"lives in package "
						+ "a00051f4-cde3-4f3f-b08e-c5ad1a5c735a\" (ENG-94374 manual testing), which is what a "
						+ "packageUId echoed into the name field looks like");
				entry["packageUId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace(
					because: "the UId stays the unambiguous identity; the name is added beside it, not instead");
			}
		});
	}

	[Test]
	[Description("Over the real MCP path, re-describing by the reported activeVersionSchemaUId reads the running version and reports it as active — the exact correction the tool description tells an agent to perform.")]
	[AllureTag(ToolName)]
	[AllureName("describe-business-process round-trips from the root to the active version by UId")]
	[AllureDescription("Proves the whole agent-facing loop on a live stand: describe by name, discover the graph is not the running one, re-describe by the reported activeVersionSchemaUId, and get a graph that reports itself as the active version with the family listed ascending.")]
	public async Task DescribeProcess_Should_ReportActiveVersion_WhenReDescribedByTheReportedUId() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		VersionFamily family = await BuildFamilyWithActiveVersionAsync(context);
		JsonObject root = ReadDescribedGraph(await CallToolAsync(context, new Dictionary<string, object?> {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = family.RootName
		}));
		string activeVersionUId = root["activeVersionSchemaUId"]!.GetValue<string>();

		// Act — the UId comes from the previous answer, never from a constant: the platform allocates it when
		// the version is saved.
		CallToolResult callResult = await AllureApi.Step($"Re-describe {activeVersionUId} through MCP", () =>
			CallToolAsync(context, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-uid"] = activeVersionUId
			}));

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a structured envelope should be returned, not a transport error");
		JsonObject active = ReadDescribedGraph(callResult);
		AllureApi.Step("Assert the re-described graph is the running version", () => {
			active["isActiveVersion"]!.GetValue<bool>().Should().BeTrue(
				because: "following the reported pointer must land on the version the runtime executes");
			active["name"]!.GetValue<string>().Should().Be(family.ActiveName,
				because: "the pointer named this schema, and describing by its UId must return that same schema");
			active["versionRootSchemaUId"]!.GetValue<string>().Should().Be(root["schemaUId"]!.GetValue<string>(),
				because: "a version reports the root it descends from, which is how a caller reaches its siblings");
		});
		AllureApi.Step("Assert the family is listed ascending from the version too", () => {
			VersionNumbers(active).Should().ContainInOrder(new int?[] { 0, 1 },
				"the family is reported ascending by version from any member, not only from the root");
		});
	}

	[Test]
	[Description("ENG-103023: over the real MCP path, mode=summary, mode=versions and element=<name> each answer with the mode they name, the summary and version views stay far below the ~20 KB spill line, and an explicit mode=full is still the whole graph.")]
	[AllureTag(ToolName)]
	[AllureName("describe-business-process answers summary, versions, element and full modes")]
	[AllureDescription("Describes the configured sandbox process four times - summary, versions, the first element by name, and full - and asserts each reply names its mode, the compact replies are under 8 KB on the wire, and full carries the element graph.")]
	public async Task DescribeProcess_Should_AnswerEachMode_ForKnownProcess() {
		// Arrange
		await using ArrangeContext context = await ArrangeAsync(requireReachableEnvironment: true);
		Dictionary<string, object?> Args(string key, string value) => new() {
			["environment-name"] = context.EnvironmentName,
			["process-name"] = context.ProcessCode,
			[key] = value
		};

		// Act
		CallToolResult summaryResult = await CallToolAsync(context, Args("mode", "summary"));
		CallToolResult versionsResult = await CallToolAsync(context, Args("mode", "versions"));
		CallToolResult fullResult = await CallToolAsync(context, Args("mode", "full"));
		JsonObject summary = ReadDescribedGraph(summaryResult);
		JsonObject versions = ReadDescribedGraph(versionsResult);
		JsonObject full = ReadDescribedGraph(fullResult);
		string firstElement = full["elements"]!.AsArray()[0]!["name"]!.GetValue<string>();
		CallToolResult elementResult = await CallToolAsync(context, Args("element", firstElement));
		JsonObject element = ReadDescribedGraph(elementResult);

		// Assert
		summary["mode"]!.GetValue<string>().Should().Be("summary", because: "the reply names its mode");
		summary["elements"]!.AsArray().Count.Should().BeGreaterThan(0, because: "the summary lists the elements");
		summary["checksum"].Should().NotBeNull(because: "the summary carries the content checksum");
		versions["mode"]!.GetValue<string>().Should().Be("versions", because: "the reply names its mode");
		versions["versions"].Should().NotBeNull(because: "the version view lists the family");
		element["mode"]!.GetValue<string>().Should().Be("element", because: "the reply names its mode");
		element["element"]!["name"]!.GetValue<string>().Should().Be(firstElement,
			because: "the element view returns the element that was asked for");
		full["elements"]!.AsArray()[0]!["buildType"].Should().NotBeNull(
			because: "an explicit full is the whole graph, as before ENG-103023");
		foreach (CallToolResult compact in new[] { summaryResult, versionsResult }) {
			JsonSerializer.Serialize(compact).Length.Should().BeLessThan(8_192,
				because: "the compact views exist to stay far below the spill line");
		}
	}

	// One reader for all three process-designer fixtures: the escaping trap it exists for caught the two
	// siblings that hand-rolled a substring check instead.
	private static JsonObject ReadDescribedGraph(CallToolResult callResult) =>
		DescribedProcessGraph.Read(callResult);

	private static IEnumerable<int?> VersionNumbers(JsonObject graph) =>
		graph["versions"]!.AsArray().Select(member => (int?)member!["version"]?.GetValue<int>());

	// The family the version tests read: a process, one version of it saved in Custom, and that version made actual,
	// so the root is no longer the member that runs. The version's name is read from the response that created it:
	// the platform composes it, and the contract does not promise how.
	private static async Task<VersionFamily> BuildFamilyWithActiveVersionAsync(ArrangeContext context) {
		string rootName = $"UsrClioBpFamilyE2e{Guid.NewGuid():N}";
		await CallToolExpectingSuccessAsync(context, CreateBusinessProcessTool.CreateBusinessProcessToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["descriptor"] = BuildFamilyRootDescriptor(rootName)
			}, "the family root has to exist before a version of it can be saved");
		CallToolResult saved = await CallToolExpectingSuccessAsync(context,
			ModifyProcessAsNewVersionTool.ModifyProcessAsNewVersionToolName, new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["process-name"] = rootName,
				["package-name"] = "Custom"
			}, "the family needs a saved version beside its root");
		string activeName = ProcessDesignerE2EArrange.CreatedVersionName(saved);
		await CallToolExpectingSuccessAsync(context, SetActiveProcessVersionTool.SetActiveProcessVersionToolName,
			new Dictionary<string, object?> {
				["environment-name"] = context.EnvironmentName,
				["version-name"] = activeName
			}, "the saved version has to become the actual one, or the root is still the member that runs");
		return new VersionFamily(rootName, activeName);
	}

	// The exit code, not IsError: the transport reports isError null for a tool that ran and failed, so an arrange
	// checked only for a transport error passes on a stand where nothing was built.
	private static async Task<CallToolResult> CallToolExpectingSuccessAsync(ArrangeContext context, string toolName,
			Dictionary<string, object?> args, string because) {
		CallToolResult result = await context.Session.CallToolAsync(toolName,
			new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
		ProcessDesignerE2ESupport.ShouldHaveLanded(result, because);
		return result;
	}

	private static string BuildFamilyRootDescriptor(string processName) =>
		$$"""
		{
		  "name": "{{processName}}",
		  "caption": "Clio BP Version Family E2E",
		  "packageName": "Custom",
		  "elements": [
		    { "name": "StartEvent1", "type": "startEvent" },
		    { "name": "task1", "type": "performTask" },
		    { "name": "EndEvent1", "type": "endEvent" }
		  ],
		  "flows": [
		    { "source": "StartEvent1", "target": "task1" },
		    { "source": "task1", "target": "EndEvent1" }
		  ]
		}
		""";

	private sealed record VersionFamily(string RootName, string ActiveName);

	// The fixtures above assert on the whole graph, so they ask for it: without a mode a graph whose reply would
	// pass 18,000 bytes comes back as the summary (ENG-103023). The mode tests pass their own mode.
	private static async Task<CallToolResult> CallToolAsync(ArrangeContext context, Dictionary<string, object?> args) {
		if (!args.ContainsKey("mode") && !args.ContainsKey("element")) {
			args["mode"] = "full";
		}
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);
		toolNames.Should().Contain(ToolName,
			because: "the describe-business-process tool must be discoverable via the get-tool-contract compact index before the end-to-end call");
		return await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> { ["args"] = args },
			context.CancellationTokenSource.Token);
	}

	private static async Task<ArrangeContext> ArrangeAsync(bool requireReachableEnvironment) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string? environmentName = settings.Sandbox.EnvironmentName;
		string? processCode = settings.Sandbox.ProcessCode;
		if (requireReachableEnvironment) {
			if (string.IsNullOrWhiteSpace(environmentName) || string.IsNullOrWhiteSpace(processCode)) {
				Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName and McpE2E:Sandbox:ProcessCode (a process that exists on the stand) to run describe-business-process MCP E2E.");
			}
			if (!await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, environmentName!)) {
				Assert.Ignore($"describe-business-process MCP E2E requires a reachable configured sandbox environment. '{environmentName}' was not reachable.");
			}
		}
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource, environmentName, processCode);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName,
		string? ProcessCode) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
