using Allure.Net.Commons;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

/// <summary>Native package SQL lifecycle proof through the real MCP process on a disposable PostgreSQL instance.</summary>
[TestFixture]
[Category("E2E")]
[Category("McpE2E.Sandbox")]
[NonParallelizable]
public sealed class SqlSchemaLifecycleE2ETests : McpContractFixtureBase {
	private string _environmentName = null!;

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		if (!settings.AllowDestructiveMcpTests || string.IsNullOrWhiteSpace(settings.Sandbox.EnvironmentName)) {
			Assert.Ignore("Configure an explicitly disposable sandbox and AllowDestructiveMcpTests=true.");
		}
		TestConfiguration.RequirePostgreSqlSandbox(settings);
		_environmentName = settings.Sandbox.EnvironmentName!;
	}

	[Test]
	[Description("Creates, reads, updates and executes native package SQL; proves persisted bodies, dry-run, duplicate rejection and actual database effects.")]
	public async Task SqlScripts_ShouldCompleteNativeLifecycle_WhenUsingDisposablePostgreSql() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		string name = "UsrSqlE2E" + Guid.NewGuid().ToString("N")[..12];
		string table = name.ToLowerInvariant();
		string initialBody = $"CREATE TABLE {table} (value integer); INSERT INTO {table} VALUES (1610);";
		async Task<T> Call<T>(string tool, Dictionary<string, object?> args) {
			args["environment-name"] = _environmentName;
			args["schema-name"] = name;
			var result = await context.Session.CallToolAsync(tool,
				new Dictionary<string, object?> { ["args"] = args }, context.CancellationTokenSource.Token);
			result.IsError.Should().NotBeTrue(because: "the tool must return its structured command result");
			return EntitySchemaStructuredResultParser.Extract<T>(result);
		}

		// Act
		SqlSchemaCreateResponse created = await Call<SqlSchemaCreateResponse>(SqlSchemaCreateTool.ToolName,
			new() { ["package-name"] = "Custom" });
		AllureApi.Step("Assert native creation succeeded", () => created.Success.Should().BeTrue(because: created.Error ?? "native SaveSchema creates the script"));
		SqlSchemaCreateResponse duplicate = await Call<SqlSchemaCreateResponse>(SqlSchemaCreateTool.ToolName,
			new() { ["package-name"] = "Custom", ["db-engine-type"] = 2 });
		AllureApi.Step("Assert duplicate did not overwrite", () => duplicate.Success.Should().BeFalse(because: "creation must not overwrite an existing package script"));
		SqlSchemaUpdateResponse updated = await Call<SqlSchemaUpdateResponse>(SqlSchemaUpdateTool.ToolName,
			new() { ["body"] = initialBody });
		AllureApi.Step("Assert body save succeeded", () => updated.Success.Should().BeTrue(because: updated.Error ?? "the native script accepts its body"));
		SqlSchemaUpdateResponse dryRun = await Call<SqlSchemaUpdateResponse>(SqlSchemaUpdateTool.ToolName,
			new() { ["body"] = "SELECT 0;", ["dry-run"] = true });
		AllureApi.Step("Assert dry-run succeeded", () => dryRun.Success.Should().BeTrue(because: "the existing script resolves without saving"));
		SqlSchemaGetResponse read = await Call<SqlSchemaGetResponse>(SqlSchemaGetTool.ToolName, new());
		AllureApi.Step("Assert durable body survived dry-run", () => read.Body.Should().Be(initialBody, because: "a separate read must observe the saved body unchanged"));
		AllureApi.Step("Assert stable script identity", () => read.SchemaUId.Should().Be(created.SchemaUId, because: "update must preserve the package script identity"));
		SqlSchemaInstallResponse installed = await Call<SqlSchemaInstallResponse>(SqlSchemaInstallTool.ToolName, new());
		AllureApi.Step("Assert native execution succeeded", () => installed.Success.Should().BeTrue(because: installed.Error ?? "the native installer runs the selected script"));
		string assertionBody = $"DO $$ BEGIN IF (SELECT count(*) FROM {table} WHERE value = 1610) <> 1 THEN RAISE EXCEPTION 'SQL effect missing'; END IF; END $$; DROP TABLE {table};";
		SqlSchemaUpdateResponse verifier = await Call<SqlSchemaUpdateResponse>(SqlSchemaUpdateTool.ToolName,
			new() { ["body"] = assertionBody });
		AllureApi.Step("Assert verification SQL persisted", () => verifier.Success.Should().BeTrue(because: verifier.Error ?? "the verifier must replace the original body"));
		SqlSchemaInstallResponse verified = await Call<SqlSchemaInstallResponse>(SqlSchemaInstallTool.ToolName, new());

		// Assert
		AllureApi.Step("Assert actual database effect", () => verified.Success.Should().BeTrue(because: verified.Error ?? "the database assertion proves the previous script created exactly one marker row"));
		SqlSchemaGetResponse finalRead = await Call<SqlSchemaGetResponse>(SqlSchemaGetTool.ToolName, new());
		AllureApi.Step("Assert latest body is durable", () => finalRead.Body.Should().Be(assertionBody, because: "execution must preserve the updated source"));
	}
}
