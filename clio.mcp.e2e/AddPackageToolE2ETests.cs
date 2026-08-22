using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>End-to-end coverage for the add-package MCP tool.</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("add-package")]
[NonParallelizable]
public sealed class AddPackageToolE2ETests : McpContractFixtureBase {
	private const string ToolName = WorkspacePackageTool.AddPackageToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("add-package as-app creates localization-ready package primitives")]
	[AllureDescription("Invokes the real add-package MCP tool in an empty workspace and verifies the application descriptor, injectable localizable-string adapter, source-code schema, ownership documentation, and example localizable value.")]
	[Description("Creates localization-ready primitives through the real add-package MCP tool when as-app is true.")]
	public async Task AddPackage_ShouldCreateLocalizationPrimitives_WhenAsAppIsTrue() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string rootDirectory = CreateFixtureDirectory("add-package");
		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		string packageName = $"Pkg{Guid.NewGuid():N}"[..18];
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(settings,
			["create-workspace", workspaceName, "--empty", "--directory", rootDirectory]);
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await AllureApi.Step("Invoke add-package through MCP", async () =>
			await arrangeContext.Session.CallToolAsync(ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["name"] = packageName,
						["workspace-path"] = workspacePath,
						["as-app"] = true
					}
				}, arrangeContext.CancellationTokenSource.Token));
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a valid local package creation must succeed");
		execution.ExitCode.Should().Be(0, because: "add-package should complete successfully");
		execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: "successful MCP execution must expose informational command output");
		string packagePath = Path.Combine(workspacePath, "packages", packageName);
		File.Exists(Path.Combine(packagePath, "Files", "app-descriptor.json")).Should().BeTrue(
			because: "as-app must create the application descriptor");
		string schemaName = $"{packageName}LocalizableStrings";
		string schema = await File.ReadAllTextAsync(Path.Combine(packagePath, "Schemas", schemaName,
			$"{schemaName}.cs"));
		string resources = await File.ReadAllTextAsync(Path.Combine(packagePath, "Resources",
			$"{schemaName}.SourceCode", "resource.en-US.xml"));
		schema.Should().Contain("no more natural schema owner",
			because: "the generated schema must explain its narrow ownership");
		resources.Should().Contain("LocalizableStrings.PackageLevelExample.Value",
			because: "the generated package must contain a concrete localization example");
		string sourceRoot = Path.Combine(packagePath, "Files", "src", "cs");
		string resolver = await File.ReadAllTextAsync(Path.Combine(sourceRoot, "LocalizableStrings",
			"LocalizableStringResolver.cs"));
		string application = await File.ReadAllTextAsync(Path.Combine(sourceRoot, $"{packageName}App.cs"));
		resolver.Should().Contain("interface ILocalizableStringResolver",
			because: "generated application code must depend on an injectable abstraction");
		resolver.Should().Contain("class LocalizableStringResolver : ILocalizableStringResolver",
			because: "the conventional implementation should be colocated with its small interface");
		resolver.Should().Contain("new LocalizableString(",
			because: "only the concrete adapter should construct the Creatio Core type");
		resolver.Should().Contain("LocalizableString localizableString = Create(",
			because: "the generated reference code must keep return values inspectable in a debugger");
		resolver.Should().Contain("throwIfNoManager: false",
			because: "the generated adapter must document the missing-manager behavior at the call site");
		application.Should().Contain("AddTransient<LocalizableStrings.ILocalizableStringResolver",
			because: "the application composition root must register the generated adapter");
	}
}
