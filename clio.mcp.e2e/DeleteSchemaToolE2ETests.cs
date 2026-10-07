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
[TestFixture]
[Category("McpE2E.Sandbox")]
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
					output.Should().Contain(message => message.Value != null
						&& message.Value.Contains($"Schemas/{schemaName}/"),
						because: "in file system mode the schema folder is either removed or named as left behind");
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
