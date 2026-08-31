using System.Threading;
using System.Threading.Tasks;
using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature(PackageFileTool.ListPackageFilesToolName)]
[NonParallelizable]
public sealed class PackageFileToolE2ETests {
	private const string CompiledPackageName = "IntegrationV2";
	private const string CompiledSourcePath = "cs/EmailClient.cs";

	[Test]
	[Description("Starts the real MCP server, lists ClioGate package files, and reads source plus the generated project from a reachable sandbox.")]
	[AllureTag(PackageFileTool.ListPackageFilesToolName)]
	[AllureTag(PackageFileTool.GetPackageFileToolName)]
	[AllureName("Package file tools list and read non-FSM compilation artifacts")]
	[AllureDescription("Runs the real clio MCP server against a reachable sandbox and proves that a compiled package's exact source and generated project are available through the lazy read-only tool surface.")]
	public async Task PackageFileTools_ShouldListAndReadCompiledPackageArtifacts() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await AllureApi.Step(
			"Arrange the real clio MCP server session",
			async () => await McpServerSession.StartAsync(settings, cancellationTokenSource.Token));
		string environmentName = await AllureApi.Step(
			"Arrange a reachable registered Creatio sandbox",
			async () => await ResolveReachableEnvironmentAsync(settings));
		IReadOnlyCollection<string> toolNames = await AllureApi.Step(
			"Discover the reachable lazy MCP tool catalog",
			async () => await session.ListReachableToolNamesAsync(cancellationTokenSource.Token));

		// Act
		CallToolResult listResult = await AllureApi.Step(
			"Invoke list-package-files through MCP",
			async () => await session.CallToolAsync(
				PackageFileTool.ListPackageFilesToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["package-name"] = CompiledPackageName
					}
				}, cancellationTokenSource.Token));
		PackageFileListResponse listResponse =
			EntitySchemaStructuredResultParser.Extract<PackageFileListResponse>(listResult);
		CallToolResult getResult = await AllureApi.Step(
			"Invoke get-package-file through MCP",
			async () => await session.CallToolAsync(
				PackageFileTool.GetPackageFileToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["package-name"] = CompiledPackageName,
						["file-path"] = CompiledSourcePath
					}
				}, cancellationTokenSource.Token));
		PackageFileContentResponse contentResponse =
			EntitySchemaStructuredResultParser.Extract<PackageFileContentResponse>(getResult);

		// Assert
		AllureApi.Step("Assert list-package-files is reachable", () =>
			toolNames.Should().Contain(PackageFileTool.ListPackageFilesToolName,
				because: "the list tool must be reachable through the lazy MCP surface"));
		AllureApi.Step("Assert get-package-file is reachable", () =>
			toolNames.Should().Contain(PackageFileTool.GetPackageFileToolName,
				because: "the content tool must be reachable through the lazy MCP surface"));
		AllureApi.Step("Assert the list invocation has no protocol error", () =>
			listResult.IsError.Should().NotBeTrue(
				because: "the reachable sandbox should satisfy the read-only tool call"));
		AllureApi.Step("Assert package file listing succeeds", () =>
			listResponse.Success.Should().BeTrue(
				because: "ClioGate should list materialized files for a compiled package"));
		AllureApi.Step("Assert the compiled source path is listed", () =>
			listResponse.Files.Should().Contain(CompiledSourcePath,
				because: "the package source is materialized in its Files directory"));
		AllureApi.Step("Assert the content invocation has no protocol error", () =>
			getResult.IsError.Should().NotBeTrue(
				because: "the source read should return a structured response"));
		AllureApi.Step("Assert source and project reads succeed", () =>
			contentResponse.Success.Should().BeTrue(
				because: "both source and generated project should be readable"));
		AllureApi.Step("Assert exact source content is returned", () =>
			contentResponse.Content.Should().Contain("namespace IntegrationV2",
				because: "the requested source must be returned rather than a path or encoded JSON string"));
		AllureApi.Step("Assert the generated project path is returned", () =>
			contentResponse.ProjectFilePath.Should().Be("IntegrationV2.csproj",
				because: "the generated project follows Creatio's package naming convention"));
		AllureApi.Step("Assert the generated project content is returned", () =>
			contentResponse.ProjectContent.Should().Contain("<Project",
				because: "the response must include the generated non-FSM project content"));
	}

	[TestCase("../web.config")]
	[TestCase("C:/Windows/web.config")]
	[Description("Starts the real MCP server and verifies get-package-file rejects unsafe paths without returning content.")]
	[AllureTag(PackageFileTool.GetPackageFileToolName)]
	[AllureName("Package file content rejects unsafe paths")]
	[AllureDescription("Calls get-package-file through the real stdio MCP server with a non-package-relative path and verifies the structured refusal contains no file or project content.")]
	public async Task GetPackageFile_ShouldRejectUnsafePathWithoutReturningContent(string unsafePath) {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(3));
		await using McpServerSession session = await AllureApi.Step(
			"Arrange the real clio MCP server session for invalid input",
			async () => await McpServerSession.StartAsync(settings, cancellationTokenSource.Token));
		string environmentName = await AllureApi.Step(
			"Arrange a reachable registered Creatio sandbox for invalid input",
			async () => await ResolveReachableEnvironmentAsync(settings));

		// Act
		CallToolResult result = await AllureApi.Step(
			"Invoke get-package-file with an unsafe path",
			async () => await session.CallToolAsync(
				PackageFileTool.GetPackageFileToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["package-name"] = CompiledPackageName,
						["file-path"] = unsafePath
					}
				}, cancellationTokenSource.Token));
		PackageFileContentResponse response =
			EntitySchemaStructuredResultParser.Extract<PackageFileContentResponse>(result);

		// Assert
		AllureApi.Step("Assert the unsafe path returns a structured MCP response", () =>
			result.IsError.Should().NotBeTrue(
				because: "caller-correctable invalid input belongs in the tool's structured response"));
		AllureApi.Step("Assert the unsafe path is refused", () =>
			response.Success.Should().BeFalse(
				because: "a parent-relative path must never be read"));
		AllureApi.Step("Assert the refusal explains package confinement", () =>
			response.Error.Should().Contain("inside the package Files directory",
				because: "the caller needs an actionable package-relative path rule"));
		AllureApi.Step("Assert no source content crosses the refusal boundary", () =>
			response.Content.Should().BeNull(
				because: "a rejected path must return no file bytes"));
		AllureApi.Step("Assert no project content crosses the refusal boundary", () =>
			response.ProjectContent.Should().BeNull(
				because: "the tool must stop before its companion project read"));
	}

	private static async Task<string> ResolveReachableEnvironmentAsync(McpE2ESettings settings) {
		string? environmentName = settings.Sandbox.EnvironmentName;
		if (string.IsNullOrWhiteSpace(environmentName)) {
			Assert.Ignore("Configure McpE2E:Sandbox:EnvironmentName to run package file MCP E2E tests.");
		}
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(
			settings, ["ping-app", "-e", environmentName]);
		if (result.ExitCode != 0) {
			Assert.Ignore($"Package file MCP E2E requires a reachable sandbox environment. '{environmentName}' was not reachable.");
		}
		return environmentName!;
	}
}
