using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end proof that a remote <c>delete-schema</c> of an entity schema reports what it leaves behind: the
/// database table always, and the package folders when the sandbox is in file system mode.
/// </summary>
/// <remarks>
/// Developer-local, like <c>DataBindingDbColorSchemaE2ETests</c>: <c>create-entity-schema</c> publishes
/// configuration and starts the asynchronous, global OData rebuild, so any test running against the same stand
/// meanwhile fails with "Creatio is currently rebuilding the OData library". <see cref="NonParallelizableAttribute"/>
/// does not bound that rebuild, so the fixture never runs in an automatic lane (see <c>clio.mcp.e2e/AGENTS.md</c>).
/// The folder cleanup itself is covered off-stand by <c>DeletedItemFileCleanerTests</c> and
/// <c>DeleteSchemaRemoteCommandTests</c>.
/// </remarks>
[TestFixture]
[Category("McpE2E.Sandbox")]
[Category("McpE2E.Manual")]
[Category("LocalOnly")]
[Explicit("Publishes a schema and starts the global OData rebuild on the shared stand; run it by hand against a leased sandbox.")]
[AllureNUnit]
[AllureFeature(DeleteSchemaTool.DeleteSchemaToolName)]
[NonParallelizable]
public sealed class DeleteSchemaToolE2ETests : McpContractFixtureBase {

	private string _environmentName = null!;
	private McpE2ESettings _settings = null!;

	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		if (!settings.AllowDestructiveMcpTests || string.IsNullOrWhiteSpace(settings.Sandbox.EnvironmentName)) {
			Assert.Ignore("Configure an explicitly disposable sandbox and AllowDestructiveMcpTests=true.");
		}
		_environmentName = settings.Sandbox.EnvironmentName!;
		_settings = settings;
	}

	[Test]
	[AllureTag(DeleteSchemaTool.DeleteSchemaToolName)]
	[AllureName("delete-schema reports the retained table and the package files of a deleted entity")]
	[Description("Creates an entity schema in a new package, deletes it with remote delete-schema, and verifies the result says the table stays and, by file system mode, either stays silent about files or names the package folders.")]
	public async Task DeleteSchema_ShouldReportRetainedTableAndPackageFiles_WhenEntitySchemaIsDeletedRemotely() {
		TeamCityRunGuard.IgnoreIfRunningUnderTeamCityOrGitHubActions(
			"create-entity-schema publishes configuration and starts the global OData rebuild, which "
			+ "makes every concurrent test on the shared stand fail with \"Creatio is currently "
			+ "rebuilding the OData library\". Run this scenario by hand against a leased sandbox.");
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(8));
		CancellationToken token = context.CancellationTokenSource.Token;
		string schemaName = "UsrDelSchema" + Guid.NewGuid().ToString("N")[..10];
		string? packageName = null;
		try {
			packageName = await CreatePackageAsync(context, "ClioE2EDel" + Guid.NewGuid().ToString("N")[..10]);
			await CreateEntitySchemaAsync(context, packageName, schemaName);
			string fileSystemMode = FsmModeStatusResultParser.Extract(await context.Session.CallToolAsync(
				FsmModeTool.GetFsmModeToolName,
				new Dictionary<string, object?> { ["environmentName"] = _environmentName }, token)).Mode;

			// Act
			// A destructive tool that is not advertised is only reachable through the clio-run executor.
			CommandExecutionEnvelope deleted = await AllureApi.Step("Delete the entity schema remotely", async () =>
				McpCommandExecutionParser.Extract(await context.Session.CallDestructiveAsync(
					DeleteSchemaTool.DeleteSchemaToolName,
					new Dictionary<string, object?> {
						["schema-name"] = schemaName,
						["environment-name"] = _environmentName,
						["remote"] = true
					}, token)));

			// Assert
			IReadOnlyList<CommandLogMessageEnvelope> output = deleted.Output ?? [];
			AllureApi.Step("Assert the delete succeeded and says the table stays", () => {
				deleted.ExitCode.Should().Be(0, because: "the platform deletes an entity schema without dependants");
				output.Should().Contain(message => message.MessageType == LogDecoratorType.Info
					&& message.Value != null
					&& message.Value.Contains($"Deleted schema '{schemaName}'")
					&& message.Value.Contains("not dropped"),
					because: "the caller must learn that the database table and its data remain");
			});
			AllureApi.Step($"Assert the package files are reported for file system mode '{fileSystemMode}'", () => {
				if (string.Equals(fileSystemMode, "on", StringComparison.OrdinalIgnoreCase)) {
					// "No folders ... were found (searched: ...)" also names the folder, so it must not satisfy this.
					bool removed = output.Any(message => message.MessageType == LogDecoratorType.Info
						&& message.Value != null
						&& message.Value.StartsWith("Removed from package folder", StringComparison.Ordinal)
						&& message.Value.Contains($"Schemas/{schemaName}/"));
					CommandLogMessageEnvelope? leftBehind = output.FirstOrDefault(message =>
						message.MessageType == LogDecoratorType.Warning
						&& message.Value != null
						&& message.Value.Contains($"Schemas/{schemaName}/"));
					if (!removed && leftBehind is not null) {
						// The runner may not reach the site folder; the fallback warning is correct, but it does not
						// prove the removal, so the run is not counted as a pass.
						Assert.Inconclusive($"The schema folder was named as left behind instead of removed: {leftBehind.Value}");
					}
					removed.Should().BeTrue(because: "in file system mode the schema folder is removed from the package");
					leftBehind.Should().BeNull(because: "a removed schema folder must not also be reported as left behind");
					return;
				}
				output.Should().NotContain(message => message.MessageType == LogDecoratorType.Warning
					&& message.Value != null
					&& message.Value.Contains("file system mode"),
					because: "outside file system mode the package files are not the source of truth");
			});
		}
		finally {
			await DeletePackageAsync(packageName);
		}
	}

	private async Task<string> CreatePackageAsync(ArrangeContext context, string requestedName) {
		return await AllureApi.Step("Create a disposable package", async () => {
			CallToolResult callResult = await context.Session.CallDestructiveAsync(CreatePackageTool.CreatePackageToolName,
				new Dictionary<string, object?> {
					["environment-name"] = _environmentName,
					["package-name"] = requestedName,
					["description"] = "clio MCP e2e delete-schema",
					["dependencies"] = new[] { "CrtBase" }
				},
				context.CancellationTokenSource.Token);
			CreatePackageResponse created = EntitySchemaStructuredResultParser.Extract<CreatePackageResponse>(callResult);
			created.Success.Should().BeTrue(because: created.Error ?? "the test needs an editable package");
			return created.PackageName!;
		});
	}

	private async Task CreateEntitySchemaAsync(ArrangeContext context, string packageName, string schemaName) {
		await AllureApi.Step("Create the entity schema to delete", async () => {
			CallToolResult callResult = await context.Session.CallToolAsync(
				CreateEntitySchemaTool.CreateEntitySchemaToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = _environmentName,
						["package-name"] = packageName,
						["schema-name"] = schemaName,
						["title-localizations"] = new Dictionary<string, string> { ["en-US"] = "Delete schema e2e" },
						["columns"] = new[] {
							new Dictionary<string, object?> {
								["column-name"] = "UsrName",
								["type"] = "Text",
								["title-localizations"] = new Dictionary<string, string> { ["en-US"] = "Name" }
							}
						}
					}
				},
				context.CancellationTokenSource.Token);
			McpCommandExecutionParser.Extract(callResult).ExitCode.Should().Be(0,
				because: "the entity schema must exist before it is deleted");
		});
	}

	private async Task DeletePackageAsync(string? packageName) {
		if (packageName is null) {
			return;
		}
		string? cleanupError = await BoundedCleanup.RunAsync(
			async token => (await ClioCliCommandRunner.RunAsync(_settings,
				["delete-pkg-remote", packageName, "-e", _environmentName], cancellationToken: token)).ExitCode,
			TimeSpan.FromMinutes(2), $"Deleting package {packageName}");
		if (cleanupError is not null) {
			TestContext.Progress.WriteLine(cleanupError);
		}
	}
}
