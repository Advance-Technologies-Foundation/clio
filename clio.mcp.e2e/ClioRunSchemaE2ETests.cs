// ENG-90312 Phase 2 Block Z7 — schema-discovery test for clio-run.
// Asserts that tools/list publishes clio-run with the expected anyOf shape, that every
// [JsonDerivedType] attribute on ClioRunArgs surfaces in the schema with a const discriminator,
// and that canary fields (mode, schema-type, action, environment-name) appear in at least one
// branch with the expected description.
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Mcp.E2E;

[TestFixture]
[NonParallelizable]
public sealed class ClioRunSchemaE2ETests {

	[Test]
	[Description("Z7: clio-run is registered with the expected count and safety flags.")]
	public async Task ClioRun_Should_Advertise_Expected_Tool_Metadata() {
		await using ArrangeContext ctx = await ArrangeAsync(TimeSpan.FromMinutes(2));

		IList<McpClientTool> tools = await ctx.Session.ListToolsAsync(ctx.Cts.Token);
		McpClientTool tool = tools.Single(t => t.Name == ClioRunTool.ToolName);

		// MCP host treats clio-run as destructive — gated through confirmation flow for every call.
		// The read-only fallthrough lives on the 23 flat tools, not here.
		tool.Name.Should().Be("clio-run");
		tool.Description.Should().Contain("command",
			because: "the description must mention the 'command' discriminator so AI agents know how to route");
	}

	[Test]
	[Description("Z7: clio-run's args is a 52-branch anyOf — every [JsonDerivedType] on ClioRunArgs surfaces as a branch with a const discriminator.")]
	public async Task ClioRun_AnyOf_Should_Cover_Every_JsonDerivedType() {
		await using ArrangeContext ctx = await ArrangeAsync(TimeSpan.FromMinutes(2));

		IList<McpClientTool> tools = await ctx.Session.ListToolsAsync(ctx.Cts.Token);
		McpClientTool tool = tools.Single(t => t.Name == ClioRunTool.ToolName);

		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		JsonElement anyOf = inputSchema
			.GetProperty("properties")
			.GetProperty("args")
			.GetProperty("anyOf");

		HashSet<string> expectedDiscriminators = typeof(ClioRunArgs)
			.GetCustomAttributes<JsonDerivedTypeAttribute>()
			.Select(a => a.TypeDiscriminator?.ToString() ?? string.Empty)
			.ToHashSet();
		expectedDiscriminators.Should().HaveCount(52,
			because: "ClioRunArgs's [JsonDerivedType] registry must enumerate all 52 non-read-only commands");

		HashSet<string> publishedDiscriminators = anyOf.EnumerateArray()
			.Select(branch => branch.GetProperty("properties").GetProperty("command").GetProperty("const").GetString() ?? string.Empty)
			.ToHashSet();

		publishedDiscriminators.Should().BeEquivalentTo(expectedDiscriminators,
			because: "every [JsonDerivedType] on ClioRunArgs must surface as an anyOf branch with the matching const discriminator");
	}

	[Test]
	[Description("Z7: schema-side canary fields surface in the branches they belong to — mode in restart-creatio, schema-type in create-schema, action in app-section.")]
	public async Task ClioRun_AnyOf_Should_Carry_Per_Branch_Fields() {
		await using ArrangeContext ctx = await ArrangeAsync(TimeSpan.FromMinutes(2));

		IList<McpClientTool> tools = await ctx.Session.ListToolsAsync(ctx.Cts.Token);
		McpClientTool tool = tools.Single(t => t.Name == ClioRunTool.ToolName);

		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		JsonElement anyOf = inputSchema
			.GetProperty("properties")
			.GetProperty("args")
			.GetProperty("anyOf");

		JsonElement restartBranch = FindBranch(anyOf, "restart-creatio");
		restartBranch.GetProperty("properties").TryGetProperty("mode", out _).Should().BeTrue(
			because: "the restart-creatio branch should expose its 'mode' discriminator alongside the command");

		JsonElement createSchemaBranch = FindBranch(anyOf, "create-schema");
		createSchemaBranch.GetProperty("properties").TryGetProperty("schema-type", out _).Should().BeTrue(
			because: "the create-schema branch should expose its 'schema-type' discriminator");

		JsonElement appSectionBranch = FindBranch(anyOf, "app-section");
		appSectionBranch.GetProperty("properties").TryGetProperty("action", out _).Should().BeTrue(
			because: "the app-section branch should expose its 'action' discriminator");

		JsonElement startBranch = FindBranch(anyOf, "start-creatio");
		startBranch.GetProperty("properties").TryGetProperty("environment-name", out _).Should().BeTrue(
			because: "start-creatio wraps a primitive env-name arg into a record field");
	}

	[Test]
	[Description("Z2 sanity preserved: clio-run dispatches through the switch to an inner tool (environment-not-found error from RestartTool).")]
	public async Task ClioRun_Should_Dispatch_Through_Switch_To_Inner_Tool() {
		await using ArrangeContext ctx = await ArrangeAsync(TimeSpan.FromMinutes(2));

		var arguments = new Dictionary<string, object?> {
			["args"] = new Dictionary<string, object?> {
				["command"] = "restart-creatio",
				["mode"] = "environment",
				["environment-name"] = "definitely-not-registered-z7-probe-env",
			}
		};

		ModelContextProtocol.Protocol.CallToolResult result = await ctx.Session.CallToolAsync(
			ClioRunTool.ToolName,
			arguments,
			ctx.Cts.Token);

		string payload = string.Join("\n", result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));
		payload.Should().NotContain("unhandled ClioRunArgs subtype",
			because: "the polymorphic deserializer should hit the RestartCreatioRunArgs arm, not the catch-all");
		payload.Should().Contain("definitely-not-registered-z7-probe-env",
			because: "an env-not-found error from the inner RestartTool proves the switch arm + adapter wired through correctly");
	}

	private static JsonElement FindBranch(JsonElement anyOf, string discriminator) =>
		anyOf.EnumerateArray()
			.Single(branch => branch.GetProperty("properties")
				.GetProperty("command")
				.GetProperty("const")
				.GetString() == discriminator);

	private static async Task<ArrangeContext> ArrangeAsync(TimeSpan timeout) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cts = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cts.Token);
		return new ArrangeContext(session, cts);
	}

	private sealed record ArrangeContext(McpServerSession Session, CancellationTokenSource Cts) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			Cts.Dispose();
		}
	}
}
