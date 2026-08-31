using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end coverage of the schema-transfer MCP surface (<c>export-schema</c> / <c>import-schema</c>).
/// </summary>
/// <remarks>
/// Both tools are long-tail: they are reachable through the compact <c>get-tool-contract</c> index and
/// <c>clio-run</c> rather than resident in <c>tools/list</c>, which is exactly what the discovery tests assert.
/// </remarks>
[TestFixture]
[AllureNUnit]
[AllureFeature(ExportSchemaTool.ExportSchemaToolName)]
[NonParallelizable]
public sealed class SchemaTransferToolE2ETests : McpContractFixtureBase {

	private const string ExportToolName = ExportSchemaTool.ExportSchemaToolName;
	private const string ImportToolName = ImportSchemaTool.ImportSchemaToolName;

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Exposes export-schema via the get-tool-contract compact index on the lazy tool surface.")]
	[AllureTag(ExportToolName)]
	[AllureName("export-schema is discoverable on the lazy surface")]
	public async Task ExportSchemaTool_Should_Be_Discoverable() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ExportToolName,
			because: "an MCP caller that needs to move one schema between environments must be able to find the "
				+ "tool even though it is not resident in tools/list");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Exposes import-schema via the get-tool-contract compact index on the lazy tool surface.")]
	[AllureTag(ImportToolName)]
	[AllureName("import-schema is discoverable on the lazy surface")]
	public async Task ImportSchemaTool_Should_Be_Discoverable() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		IReadOnlyCollection<string> toolNames =
			await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ImportToolName,
			because: "export without a reachable import would leave the handover half-finished");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Reports a readable failure when export-schema is called with an unknown environment name.")]
	[AllureTag(ExportToolName)]
	[AllureName("export-schema reports invalid environment failures")]
	public async Task ExportSchemaTool_Should_Report_Invalid_Environment_Failure() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string invalidEnvironmentName = $"missing-export-schema-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ExportToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["schema-name"] = "UsrMissingSchema",
					["environment-name"] = invalidEnvironmentName
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.Should().NotBeNull(
			because: "an unknown environment must produce a tool response, not a transport failure");
		// Asserting only NotBeNull would pass on a success envelope, on an empty error and on unrelated
		// HTML — none of which is the behaviour this test claims to guard. The exit code and the message
		// text are what make the failure readable, so both are asserted.
		execution.ExitCode.Should().NotBe(0,
			because: $"export-schema against a non-existent environment must report a failing exit code. Actual: {DescribeExecution(execution)}");
		DescribeExecution(execution).Should().MatchRegex(
			$"(?is)({Regex.Escape(invalidEnvironmentName)}|environment.*not.*found|not found|not registered)",
			because: $"the failure must name the missing environment instead of failing silently. Actual: {DescribeExecution(execution)}");
	}

	[Category("McpE2E.NoEnvironment")]
	[Test]
	[Description("Reports a readable failure when import-schema is pointed at a path that holds no bundle.")]
	[AllureTag(ImportToolName)]
	[AllureName("import-schema reports a missing bundle")]
	public async Task ImportSchemaTool_Should_Report_Missing_Bundle() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(3));
		string missingBundlePath = Path.Combine(Path.GetTempPath(), $"clio-missing-bundle-{Guid.NewGuid():N}");

		// Act
		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			ImportToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["path"] = missingBundlePath,
					["package-name"] = "Custom",
					["environment-name"] = $"noop-{Guid.NewGuid():N}",
					["dry-run"] = true
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.Should().NotBeNull(
			because: "a missing bundle is an ordinary input error and must stay inside the tool response envelope");
		// Same reasoning as the export case above: a bare NotBeNull cannot fail, so the exit code and the
		// message are asserted instead.
		execution.ExitCode.Should().NotBe(0,
			because: $"import-schema pointed at a path that holds no bundle must report a failing exit code. Actual: {DescribeExecution(execution)}");
		DescribeExecution(execution).Should().MatchRegex(
			$"(?is)({Regex.Escape(missingBundlePath)}|bundle|descriptor|not found|does not exist)",
			because: $"the failure must explain that the bundle is missing. Actual: {DescribeExecution(execution)}");
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Round-trips a schema through export-schema and import-schema against a LIVE environment: exports a schema the test itself created into a bundle, re-imports that bundle into its own package (a real REPLACE write), and proves the same bundle aimed at a foreign package is refused as a new layer.")]
	[AllureTag(ExportToolName)]
	[AllureTag(ImportToolName)]
	[AllureName("export-schema/import-schema round-trip against a live environment")]
	[AllureDescription("Provisions a throwaway workspace package on the sandbox stand, creates an entity schema in it via sync-schemas, then drives export-schema and import-schema through the real clio MCP server: the export must produce a bundle whose descriptor carries the live identity, the re-import into the owning package must be planned as REPLACE and actually written, and the same bundle aimed at a package that does not own the name must be refused because creating a second layer is the defect this feature exists to avoid.")]
	public async Task SchemaTransfer_Should_RoundTrip_Export_And_Import_Against_A_Live_Environment() {
		// Arrange
		await using var arrangeContext = Arrange(TimeSpan.FromMinutes(15));
		SandboxSchema sandbox = await ArrangeSandboxSchemaAsync(arrangeContext);
		string destination = Path.Combine(Path.GetTempPath(), $"clio-schema-bundle-{Guid.NewGuid():N}");
		string bundleDirectory = Path.Combine(destination, sandbox.SchemaName);
		string foreignPackageName = $"Pkg{Guid.NewGuid():N}"[..18];

		// Act
		CommandExecutionEnvelope export = McpCommandExecutionParser.Extract(
			await arrangeContext.Session.CallToolAsync(
				ExportToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["schema-name"] = sandbox.SchemaName,
						["environment-name"] = sandbox.EnvironmentName,
						["package-name"] = sandbox.PackageName,
						["destination"] = destination
					}
				},
				arrangeContext.CancellationTokenSource.Token));

		CommandExecutionEnvelope replaceImport = McpCommandExecutionParser.Extract(
			await arrangeContext.Session.CallToolAsync(
				ImportToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["path"] = bundleDirectory,
						["package-name"] = sandbox.PackageName,
						["environment-name"] = sandbox.EnvironmentName
					}
				},
				arrangeContext.CancellationTokenSource.Token));

		CommandExecutionEnvelope refusedImport = McpCommandExecutionParser.Extract(
			await arrangeContext.Session.CallToolAsync(
				ImportToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["path"] = bundleDirectory,
						["package-name"] = foreignPackageName,
						["environment-name"] = sandbox.EnvironmentName,
						["dry-run"] = true
					}
				},
				arrangeContext.CancellationTokenSource.Token));

		// Assert
		export.ExitCode.Should().Be(0,
			because: $"export-schema must read the schema the test created from the live environment. Actual: {DescribeExecution(export)}");
		Directory.Exists(bundleDirectory).Should().BeTrue(
			because: $"the export must write the bundle folder it reported. Expected at '{bundleDirectory}'. Actual: {DescribeExecution(export)}");
		string descriptorPath = Path.Combine(bundleDirectory, "descriptor.json");
		File.Exists(descriptorPath).Should().BeTrue(
			because: "a bundle without its reviewable descriptor is not the artifact export-schema promises");
		File.Exists(Path.Combine(bundleDirectory, "schema-data.json")).Should().BeTrue(
			because: "the platform payload is what import-schema actually writes, so it must be in the bundle");
		JsonElement descriptor = JsonDocument.Parse(File.ReadAllText(descriptorPath)).RootElement;
		descriptor.GetProperty("schemaName").GetString().Should().Be(sandbox.SchemaName,
			because: "the descriptor must name the schema that was exported, not a stale or copied identity");
		descriptor.GetProperty("schemaUId").GetString().Should().NotBeNullOrWhiteSpace(
			because: "the UId is what keeps the imported schema the SAME schema rather than a divergent copy");
		descriptor.GetProperty("sourcePackageName").GetString().Should().Be(sandbox.PackageName,
			because: "the export must record the package the live layer came from");

		replaceImport.ExitCode.Should().Be(0,
			because: $"re-importing the bundle into the package that owns the schema is a REPLACE and must succeed. Actual: {DescribeExecution(replaceImport)}");
		DescribeExecution(replaceImport).Should().MatchRegex(
			$"(?is)replace.*{Regex.Escape(sandbox.SchemaName)}|{Regex.Escape(sandbox.SchemaName)}.*replace",
			because: $"the plan the operator sees must say the existing layer is replaced. Actual: {DescribeExecution(replaceImport)}");
		DescribeExecution(replaceImport).Should().MatchRegex(
			"(?is)imported schema",
			because: $"a non-dry-run import must report that it actually wrote the schema. Actual: {DescribeExecution(replaceImport)}");

		refusedImport.ExitCode.Should().NotBe(0,
			because: $"the same bundle aimed at a package that does not own the name must be refused instead of silently creating a second layer. Actual: {DescribeExecution(refusedImport)}");
		DescribeExecution(refusedImport).Should().MatchRegex(
			$"(?is)(allow-new-layer|new layer|{Regex.Escape(sandbox.PackageName)})",
			because: $"the refusal must name the owning package or the opt-in flag so the operator can decide deliberately. Actual: {DescribeExecution(refusedImport)}");
	}

	/// <summary>
	/// Provisions the live fixture the round-trip needs: a throwaway workspace package on the sandbox stand and
	/// one entity schema inside it, created through <c>sync-schemas</c>.
	/// </summary>
	/// <remarks>
	/// The round-trip deliberately exports and re-imports a schema the TEST created rather than a platform one.
	/// A real (non-dry-run) import replaces a layer, so aiming it at a platform package would mutate shared
	/// configuration; the test owns this schema, so replacing it with byte-identical content is a no-op the stand
	/// can absorb. <c>Assert.Ignore</c> — never a failure — is used for every missing precondition (destructive
	/// opt-in, reachable stand, cliogate), matching <c>SchemaSyncToolE2ETests</c>: an unavailable stand is not a
	/// regression of this feature.
	/// </remarks>
	private async Task<SandboxSchema> ArrangeSandboxSchemaAsync(ArrangeContext context) {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore(
				"Set McpE2E:AllowDestructiveMcpTests=true to run the export-schema/import-schema round-trip against a live environment.");
		}

		CancellationToken cancellationToken = context.CancellationTokenSource.Token;
		string environmentName = await ResolveReachableEnvironmentAsync(settings, cancellationToken);
		try {
			await ClioCliCommandRunner.EnsureCliogateInstalledAsync(settings, environmentName, cancellationToken);
		}
		catch (Exception exception) {
			Assert.Ignore(
				$"Skipping the schema-transfer round-trip because cliogate could not be installed or verified for '{environmentName}'. {exception.Message}");
		}

		string rootDirectory = CreateFixtureDirectory("schema-transfer");
		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		string packageName = $"Pkg{Guid.NewGuid():N}"[..18];
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["create-workspace", workspaceName, "--empty", "--directory", rootDirectory],
			workingDirectory: rootDirectory,
			cancellationToken: cancellationToken);
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["add-package", packageName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["push-workspace", "-e", environmentName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["pkg-hotfix", packageName, "true", "-e", environmentName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
		await ClioCliCommandRunner.WaitForEnvironmentRecoveryAsync(settings, environmentName, cancellationToken);

		string schemaName = $"Usr{Guid.NewGuid():N}";
		CallToolResult syncResult = await context.Session.CallToolAsync(
			SchemaSyncTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["package-name"] = packageName,
					["operations"] = new object?[] {
						new Dictionary<string, object?> {
							["type"] = "create-entity",
							["schema-name"] = schemaName,
							["title-localizations"] = new Dictionary<string, object?> { ["en-US"] = "Schema Transfer Entity" },
							["columns"] = new object?[] {
								new Dictionary<string, object?> {
									["name"] = "UsrTitle",
									["type"] = "Text",
									["title-localizations"] = new Dictionary<string, object?> { ["en-US"] = "Title" }
								}
							}
						}
					}
				}
			},
			cancellationToken);
		syncResult.IsError.Should().NotBeTrue(
			because: "the round-trip needs a schema of its own on the stand; without it there is nothing to export");

		return new SandboxSchema(environmentName, packageName, schemaName);
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(
		McpE2ESettings settings,
		CancellationToken cancellationToken) {
		string? configuredEnvironmentName = settings.Sandbox.EnvironmentName;
		if (!string.IsNullOrWhiteSpace(configuredEnvironmentName)
			&& await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, configuredEnvironmentName, cancellationToken)) {
			return configuredEnvironmentName;
		}

		const string fallbackEnvironmentName = "d2";
		if (await ClioCliCommandRunner.IsEnvironmentReachableAsync(settings, fallbackEnvironmentName, cancellationToken)) {
			return fallbackEnvironmentName;
		}

		Assert.Ignore(
			$"The schema-transfer round-trip requires a reachable environment. Configured sandbox environment '{configuredEnvironmentName}' was not reachable, and fallback environment '{fallbackEnvironmentName}' was also unavailable.");
		return string.Empty;
	}

	/// <summary>The live fixture the round-trip runs against.</summary>
	private sealed record SandboxSchema(string EnvironmentName, string PackageName, string SchemaName);

	private static string DescribeExecution(CommandExecutionEnvelope execution) {
		string messages = execution.Output is null
			? "<no messages>"
			: string.Join(" | ", execution.Output.Select(message => $"{message.MessageType}: {message.Value}"));
		return $"ExitCode={execution.ExitCode}; Messages={messages}";
	}
}
