using System.Linq;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Common;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using Clio.Package;
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
		await AllureApi.Step("Arrange an empty clio workspace", async () =>
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(settings,
				["create-workspace", workspaceName, "--empty", "--directory", rootDirectory]));
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
		AllureApi.Step("Assert the MCP call has no protocol error", () =>
			callResult.IsError.Should().NotBeTrue(because: "a valid local package creation must succeed"));
		AllureApi.Step("Assert add-package succeeds", () =>
			execution.ExitCode.Should().Be(0, because: "add-package should complete successfully"));
		AllureApi.Step("Assert successful command output is reported", () =>
			execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
				because: "successful MCP execution must expose informational command output"));
		AllureApi.Step("Assert the missing-environment consequence is warned about", () =>
			execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Warning
					&& message.Value != null
					&& message.Value.Contains("No Creatio environment was resolved")
					&& message.Value.Contains("schema-name-prefix"),
				because: "without an environment the schema is generated unprefixed and Creatio would "
					+ "refuse it when it loads the package from the file system, so the caller must be told "
					+ "and given the explicit-prefix escape hatch"));
		string packagePath = Path.Combine(workspacePath, "packages", packageName);
		AllureApi.Step("Assert the application descriptor is generated", () =>
			File.Exists(Path.Combine(packagePath, "Files", "app-descriptor.json")).Should().BeTrue(
				because: "as-app must create the application descriptor"));
		string schemaName = $"{packageName}LocalizableStrings";
		string schema = await File.ReadAllTextAsync(Path.Combine(packagePath, "Schemas", schemaName,
			$"{schemaName}.cs"));
		string resources = await File.ReadAllTextAsync(Path.Combine(packagePath, "Resources",
			$"{schemaName}.SourceCode", "resource.en-US.xml"));
		AllureApi.Step("Assert the schema documents narrow ownership", () =>
			schema.Should().Contain("no more natural schema owner",
				because: "the generated schema must explain its narrow ownership"));
		AllureApi.Step("Assert the resource contains a concrete example", () =>
			resources.Should().Contain("LocalizableStrings.PackageLevelExample.Value",
				because: "the generated package must contain a concrete localization example"));
		string sourceRoot = Path.Combine(packagePath, "Files", "src", "cs");
		string resolver = await File.ReadAllTextAsync(Path.Combine(sourceRoot, "LocalizableStrings",
			"LocalizableStringResolver.cs"));
		string application = await File.ReadAllTextAsync(Path.Combine(sourceRoot, $"{packageName}App.cs"));
		AllureApi.Step("Assert the injectable resolver interface is generated", () =>
			resolver.Should().Contain("interface ILocalizableStringResolver",
				because: "generated application code must depend on an injectable abstraction"));
		AllureApi.Step("Assert the conventional resolver implementation is colocated", () =>
			resolver.Should().Contain("class LocalizableStringResolver : ILocalizableStringResolver",
				because: "the conventional implementation should be colocated with its small interface"));
		AllureApi.Step("Assert the adapter constructs the Creatio Core type", () =>
			resolver.Should().Contain("new LocalizableString(",
				because: "only the concrete adapter should construct the Creatio Core type"));
		string[] localizableStringConstructors = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
			.Where(path => File.ReadAllText(path).Contains("new LocalizableString("))
			.ToArray();
		AllureApi.Step("Assert only the adapter constructs LocalizableString", () =>
			localizableStringConstructors.Should().ContainSingle()
				.Which.Should().Be(Path.Combine(sourceRoot, "LocalizableStrings", "LocalizableStringResolver.cs"),
					because: "no generated consumer may bypass the injectable localization boundary"));
		AllureApi.Step("Assert resolver return values remain debugger-friendly", () =>
			resolver.Should().Contain("LocalizableString localizableString = Create(",
				because: "the generated reference code must keep return values inspectable in a debugger"));
		AllureApi.Step("Assert missing resource managers do not throw", () =>
			resolver.Should().Contain("throwIfNoManager: false",
				because: "the generated adapter must document the missing-manager behavior at the call site"));
		AllureApi.Step("Assert the composition root registers the resolver", () =>
			application.Should().Contain("AddTransient<LocalizableStrings.ILocalizableStringResolver",
				because: "the application composition root must register the generated adapter"));
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("add-package as-app applies an explicitly requested schema-name prefix")]
	[AllureDescription("Invokes the real add-package MCP tool with schema-name-prefix and verifies the prefix reaches the schema folder, resource folder, descriptor, and generated class without contacting Creatio.")]
	[Description("Applies the requested schema-name prefix to every generated schema artefact when schema-name-prefix is supplied.")]
	public async Task AddPackage_ShouldPrefixGeneratedSchema_WhenSchemaNamePrefixIsRequested() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string rootDirectory = CreateFixtureDirectory("add-package-prefix");
		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		string packageName = $"Pkg{Guid.NewGuid():N}"[..18];
		const string requestedPrefix = "Ktl";
		await AllureApi.Step("Arrange an empty clio workspace", async () =>
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(settings,
				["create-workspace", workspaceName, "--empty", "--directory", rootDirectory]));
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await AllureApi.Step("Invoke add-package with an explicit prefix", async () =>
			await arrangeContext.Session.CallToolAsync(ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["name"] = packageName,
						["workspace-path"] = workspacePath,
						["as-app"] = true,
						["schema-name-prefix"] = requestedPrefix
					}
				}, arrangeContext.CancellationTokenSource.Token));
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		AllureApi.Step("Assert the MCP call has no protocol error", () =>
			callResult.IsError.Should().NotBeTrue(because: "a valid local package creation must succeed"));
		AllureApi.Step("Assert add-package succeeds", () =>
			execution.ExitCode.Should().Be(0, because: "add-package should complete successfully"));
		string packagePath = Path.Combine(workspacePath, "packages", packageName);
		string schemaName = $"{requestedPrefix}{packageName}LocalizableStrings";
		string schemaDirectory = Path.Combine(packagePath, "Schemas", schemaName);
		AllureApi.Step("Assert the schema folder carries the requested prefix", () =>
			Directory.Exists(schemaDirectory).Should().BeTrue(
				because: "Creatio matches a schema by the code the folder name encodes"));
		AllureApi.Step("Assert the resource folder carries the requested prefix", () =>
			Directory.Exists(Path.Combine(packagePath, "Resources", $"{schemaName}.SourceCode"))
				.Should().BeTrue(because: "schema resources are addressed by the prefixed schema name"));
		string descriptor = await File.ReadAllTextAsync(Path.Combine(schemaDirectory, "descriptor.json"));
		AllureApi.Step("Assert the descriptor name carries the requested prefix", () =>
			descriptor.Should().Contain($"\"Name\": \"{schemaName}\"",
				because: "the descriptor name is the schema code Creatio validates the prefix against"));
		string generatedClass = await File.ReadAllTextAsync(Path.Combine(schemaDirectory, $"{schemaName}.cs"));
		AllureApi.Step("Assert the generated class carries the requested prefix", () =>
			generatedClass.Should().Contain($"public class {schemaName}",
				because: "the generated class name must match the schema code it is compiled under"));
		AllureApi.Step("Assert no unprefixed schema folder is left behind", () =>
			Directory.Exists(Path.Combine(packagePath, "Schemas", $"{packageName}LocalizableStrings"))
				.Should().BeFalse(because: "the prefix must replace the plain name, not be added beside it"));
		AllureApi.Step("Assert an explicit prefix suppresses the missing-environment warning", () =>
			execution.Output.Should().NotContain(message => message.MessageType == LogDecoratorType.Warning
					&& message.Value != null
					&& message.Value.Contains("No Creatio environment was resolved"),
				because: "an explicit prefix answers the question that warning asks, and needs no Creatio "
					+ "call; unrelated warnings must not decide this assertion"));
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("add-package rejects unsafe package names before writing")]
	[AllureDescription("Invokes the real add-package MCP tool with a traversal-shaped package name and verifies a structured failure without files outside the packages directory.")]
	[Description("Rejects a path-traversal package name through the real add-package MCP tool before writing files.")]
	public async Task AddPackage_ShouldRejectWithoutWriting_WhenPackageNameContainsPathTraversal() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		string rootDirectory = CreateFixtureDirectory("add-package-invalid-name");
		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		await AllureApi.Step("Arrange an empty clio workspace for invalid input", async () =>
			await ClioCliCommandRunner.RunAndAssertSuccessAsync(settings,
				["create-workspace", workspaceName, "--empty", "--directory", rootDirectory]));
		await using ArrangeContext arrangeContext = Arrange(TimeSpan.FromMinutes(3));

		// Act
		CallToolResult callResult = await AllureApi.Step("Invoke add-package with an unsafe name", async () =>
			await arrangeContext.Session.CallToolAsync(ToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["name"] = "../EscapedPackage",
						["workspace-path"] = workspacePath,
						["as-app"] = true
					}
				}, arrangeContext.CancellationTokenSource.Token));
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		AllureApi.Step("Assert validation remains a structured MCP result", () =>
			callResult.IsError.Should().NotBeTrue(
				because: "caller-correctable name validation must remain a structured command result"));
		AllureApi.Step("Assert unsafe package names are rejected", () =>
			execution.ExitCode.Should().Be(1,
				because: "a package name containing path traversal must be rejected"));
		AllureApi.Step("Assert the package-name contract is reported", () =>
			execution.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Error
				&& message.Value != null
				&& message.Value.Contains(PackageCreator.InvalidPackageNameMessage),
				because: "the failure must explain the accepted package-name contract"));
		AllureApi.Step("Assert invalid input cannot escape the packages directory", () =>
			Directory.Exists(Path.Combine(workspacePath, "EscapedPackage")).Should().BeFalse(
				because: "the invalid name must not escape the workspace packages directory"));
	}
}
