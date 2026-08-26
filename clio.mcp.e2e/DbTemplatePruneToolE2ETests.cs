using System.Text.Json;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Npgsql;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("database template pruning")]
public sealed class DbTemplatePruneToolE2ETests {
	[Test]
	[Description("Discovers both template-pruning tools and returns a structured configuration failure for an unknown server.")]
	[AllureTag(DbTemplatePruneTool.ListDbTemplatesToolName)]
	[AllureName("List database templates returns structured configuration failure")]
	[AllureDescription("Starts the real MCP process, discovers both tools, and calls inventory for an unknown configured server.")]
	public async Task ListDbTemplates_UnknownServer_ReturnsStructuredFailure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await AllureApi.Step("Arrange MCP server session", async () =>
			await McpServerSession.StartAsync(settings, cancellation.Token));

		// Act
		IReadOnlyCollection<string> names = await AllureApi.Step("Act by discovering reachable tools", async () =>
			await session.ListReachableToolNamesAsync(cancellation.Token));
		CallToolResult result = await AllureApi.Step("Act by inventorying an unknown server", async () =>
			await session.CallToolAsync(DbTemplatePruneTool.ListDbTemplatesToolName,
			new Dictionary<string, object?> { ["args"] = new { dbServerName = "missing-db-server" } },
			cancellation.Token));
		string serialized = JsonSerializer.Serialize(result);

		// Assert
		AllureApi.Step("Assert discovery and structured failure", () => {
		names.Should().Contain(DbTemplatePruneTool.ListDbTemplatesToolName,
			because: "the read-only inventory tool must be reachable on the lazy MCP surface");
		names.Should().Contain(DbTemplatePruneTool.PruneDbTemplatesToolName,
			because: "the destructive companion tool must be discoverable before an approved call");
		result.IsError.Should().NotBeTrue(
			because: "configuration failures belong in the structured tool result, not the MCP transport envelope");
		serialized.Should().Contain("configuration",
			because: "automation must distinguish an empty inventory from a configuration failure");
		});
	}

	[Test]
	[Description("Requires approval instead of executing a direct destructive template-pruning call.")]
	[AllureTag(DbTemplatePruneTool.PruneDbTemplatesToolName)]
	[AllureName("Raw destructive prune requires confirmation")]
	[AllureDescription("Calls the long-tail destructive tool by raw name and verifies the durable approval gate prevents execution.")]
	public async Task PruneDbTemplates_DirectCall_RequiresConfirmation() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await AllureApi.Step("Arrange MCP server session", async () =>
			await McpServerSession.StartAsync(settings, cancellation.Token));

		// Act
		CallToolResult result = await AllureApi.Step("Act by calling destructive tool through raw name", async () =>
			await session.CallToolRawAsync(DbTemplatePruneTool.PruneDbTemplatesToolName,
			new Dictionary<string, object?> {
				["args"] = new { dbServerName = "missing-db-server", databaseNames = new[] { "template-a" } }
			}, cancellation.Token));
		string serialized = JsonSerializer.Serialize(result);

		// Assert
		AllureApi.Step("Assert confirmation is required without execution", () => {
		serialized.Should().Contain("confirmation-required",
			because: "direct long-tail destructive calls must never bypass the host approval flow");
		serialized.Should().NotContain("\"outcome\":\"deleted\"",
			because: "the raw approval response must not contain evidence of executed deletion");
		});
	}

	[Test]
	[Description("Returns a structured validation failure through the approved destructive executor for an empty selection.")]
	[AllureTag(DbTemplatePruneTool.PruneDbTemplatesToolName)]
	[AllureName("Approved prune rejects an empty selection")]
	[AllureDescription("Routes through clio-run-destructive and verifies an empty explicit selection fails before server access.")]
	public async Task PruneDbTemplates_EmptySelection_ReturnsValidationFailure() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await AllureApi.Step("Arrange MCP server session", async () =>
			await McpServerSession.StartAsync(settings, cancellation.Token));

		// Act
		CallToolResult result = await AllureApi.Step("Act by calling approved executor with empty selection", async () =>
			await session.CallDestructiveAsync(DbTemplatePruneTool.PruneDbTemplatesToolName,
			new Dictionary<string, object?> {
				["dbServerName"] = "missing-db-server",
				["databaseNames"] = Array.Empty<string>()
			}, cancellation.Token));
		string serialized = JsonSerializer.Serialize(result);

		// Assert
		AllureApi.Step("Assert structured validation failure", () => {
		result.IsError.Should().NotBeTrue(
			because: "request validation failures should use the structured tool contract");
		serialized.Should().Contain("validation",
			because: "an empty explicit selection must fail before server resolution or deletion");
		});
	}
}

[TestFixture]
[Category("McpE2E.Sandbox")]
[NonParallelizable]
[AllureNUnit]
[AllureFeature("database template pruning")]
public sealed class DbTemplatePruneSandboxE2ETests {
	[Test]
	[Description("Inventories and deletes an explicitly named managed template through the approved MCP path on an opt-in PostgreSQL sandbox.")]
	[AllureTag(DbTemplatePruneTool.PruneDbTemplatesToolName)]
	[AllureName("Prune database template deletes only the approved sandbox template")]
	[AllureDescription("Creates uniquely named managed PostgreSQL templates, verifies inventory and raw-call gating, deletes one through clio-run-destructive, and verifies the database side effect.")]
	public async Task PruneDbTemplates_ApprovedSandboxCall_DeletesTemplate() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run database-template deletion E2E tests.");
		}
		TestConfiguration.EnsureSandboxIsConfigured(settings);
		TestConfiguration.RequirePostgreSqlSandbox(settings);
		settings.Sandbox.DeploymentDbServerName.Should().NotBeNullOrWhiteSpace(
			because: "the destructive test must name the configured local PostgreSQL server it owns");
		SandboxEnvironmentContext sandbox = SandboxEnvironmentResolver.Resolve(settings);
		NpgsqlConnectionStringBuilder builder = new(sandbox.DatabaseConnectionString) { Database = "postgres" };
		string approvedName = $"clio_prune_e2e_\"_{Guid.NewGuid():N}";
		string gatedName = $"clio_prune_gate_{Guid.NewGuid():N}";

		try {
			await AllureApi.Step("Arrange unique managed PostgreSQL templates", async () => {
				await CreateManagedTemplateAsync(builder.ConnectionString, approvedName);
				await CreateManagedTemplateAsync(builder.ConnectionString, gatedName);
			});
			using CancellationTokenSource cancellation = new(TimeSpan.FromMinutes(3));
			await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellation.Token);
			DbTemplateInventoryResult inventory = await AllureApi.Step("Act by inventorying sandbox templates", async () => {
				CallToolResult result = await session.CallToolAsync(DbTemplatePruneTool.ListDbTemplatesToolName,
					new Dictionary<string, object?> {
						["args"] = new { dbServerName = settings.Sandbox.DeploymentDbServerName }
					}, cancellation.Token);
				string content = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
				return JsonSerializer.Deserialize<DbTemplateInventoryResult>(content,
					new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
			});
			AllureApi.Step("Assert both managed templates are inventoried", () => {
				inventory.Templates.Select(template => template.DatabaseName).Should().Contain(approvedName,
					because: "the quoted identifier template has valid clio metadata");
				inventory.Templates.Select(template => template.DatabaseName).Should().Contain(gatedName,
					because: "the approval-gate control template has valid clio metadata");
			});

			CallToolResult rawResult = await AllureApi.Step("Act by calling destructive prune through the raw name", async () =>
				await session.CallToolRawAsync(DbTemplatePruneTool.PruneDbTemplatesToolName,
					new Dictionary<string, object?> {
						["args"] = new {
							dbServerName = settings.Sandbox.DeploymentDbServerName,
							databaseNames = new[] { gatedName }
						}
					}, cancellation.Token));
			AllureApi.Step("Assert raw call is gated and leaves its template present", () => {
				JsonSerializer.Serialize(rawResult).Should().Contain("confirmation-required",
					because: "the raw destructive call needs host approval");
				DatabaseExistsAsync(builder.ConnectionString, gatedName).GetAwaiter().GetResult().Should().BeTrue(
					because: "the unapproved destructive call must not delete its target");
			});

			CallToolResult pruneResult = await AllureApi.Step("Act by deleting the approved template", async () =>
				await session.CallDestructiveAsync(DbTemplatePruneTool.PruneDbTemplatesToolName,
					new Dictionary<string, object?> {
						["dbServerName"] = settings.Sandbox.DeploymentDbServerName,
						["databaseNames"] = new[] { approvedName }
					}, cancellation.Token));
			await AllureApi.Step("Assert approved template was deleted", async () => {
				JsonSerializer.Serialize(pruneResult).Should().Contain("complete-success",
					because: "the explicitly approved sandbox template should be deleted");
				(await DatabaseExistsAsync(builder.ConnectionString, approvedName)).Should().BeFalse(
					because: "the PostgreSQL catalog must confirm the destructive side effect");
			});
		}
		finally {
			await CleanupAsync(builder.ConnectionString, [approvedName, gatedName]);
		}
	}

	private static async Task CreateManagedTemplateAsync(string connectionString, string name) {
		string quoted = QuoteIdentifier(name);
		string metadata = $"sourceFile:e2e|createdDate:{DateTime.UtcNow:o}|version:1.0".Replace("'", "''");
		await using NpgsqlConnection connection = new(connectionString);
		await connection.OpenAsync();
		await using NpgsqlCommand command = connection.CreateCommand();
		command.CommandText = $"CREATE DATABASE {quoted};";
		await command.ExecuteNonQueryAsync();
		command.CommandText = $"COMMENT ON DATABASE {quoted} IS '{metadata}';";
		await command.ExecuteNonQueryAsync();
		command.CommandText = $"ALTER DATABASE {quoted} IS_TEMPLATE true;";
		await command.ExecuteNonQueryAsync();
	}

	private static async Task<bool> DatabaseExistsAsync(string connectionString, string name) {
		await using NpgsqlConnection connection = new(connectionString);
		await connection.OpenAsync();
		await using NpgsqlCommand command = new("SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname = @name);", connection);
		command.Parameters.AddWithValue("name", name);
		return (bool)(await command.ExecuteScalarAsync())!;
	}

	private static async Task DropIfExistsAsync(string connectionString, string name) {
		if (!await DatabaseExistsAsync(connectionString, name)) {
			return;
		}
		string quoted = QuoteIdentifier(name);
		await using NpgsqlConnection connection = new(connectionString);
		await connection.OpenAsync();
		await using NpgsqlCommand command = connection.CreateCommand();
		command.CommandText = $"ALTER DATABASE {quoted} IS_TEMPLATE false;";
		await command.ExecuteNonQueryAsync();
		command.CommandText = $"DROP DATABASE {quoted};";
		await command.ExecuteNonQueryAsync();
	}

	private static async Task CleanupAsync(string connectionString, IReadOnlyList<string> names) {
		List<Exception> failures = [];
		foreach (string name in names) {
			try {
				await DropIfExistsAsync(connectionString, name);
			}
			catch (Exception exception) {
				failures.Add(exception);
			}
		}
		if (failures.Count > 0) {
			throw new AggregateException("Failed to clean one or more database-template E2E fixtures.", failures);
		}
	}

	private static string QuoteIdentifier(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";
}
