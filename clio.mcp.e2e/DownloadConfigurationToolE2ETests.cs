using System.Text.RegularExpressions;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the download-configuration MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("download-configuration")]
[NonParallelizable]
public sealed class DownloadConfigurationToolE2ETests {
	private const string EnvironmentToolName = DownloadConfigurationTool.DownloadConfigurationByEnvironmentToolName;
	private const string BuildToolName = DownloadConfigurationTool.DownloadConfigurationByBuildToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes download-configuration-by-build against a synthetic extracted Creatio directory, and verifies that the workspace `.application` content is created.")]
	[AllureTag(BuildToolName)]
	[AllureName("Download configuration by build populates workspace application folders")]
	[AllureDescription("Uses the real clio MCP server to call download-configuration-by-build with a temporary workspace and extracted Creatio directory, then verifies copied root assemblies, configuration DLLs, and package content.")]
	public async Task DownloadConfigurationByBuild_Should_Populate_Workspace_Application_Content() {
		// Arrange
		await using DownloadConfigurationArrangeContext arrangeContext = await ArrangeBuildAsync();

		// Act
		DownloadConfigurationActResult actResult = await ActBuildAsync(arrangeContext);

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult, 0, "the synthetic extracted build should be processed successfully");
		AssertIncludesInfoMessage(actResult, "successful dconf build execution should emit info messages");
		AssertNetCoreArtifactsWereCopied(arrangeContext);
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes download-configuration-by-environment with a missing environment name, and verifies that MCP reports a readable failure without mutating the workspace.")]
	[AllureTag(EnvironmentToolName)]
	[AllureName("Download configuration by environment reports unknown environment failures")]
	[AllureDescription("Uses the real clio MCP server to call download-configuration-by-environment with a guaranteed-missing environment name and verifies the resolver failure plus the absence of downloaded workspace artifacts.")]
	public async Task DownloadConfigurationByEnvironment_Should_Report_Invalid_Environment() {
		// Arrange
		await using DownloadConfigurationArrangeContext arrangeContext = await ArrangeEnvironmentFailureAsync();

		// Act
		DownloadConfigurationActResult actResult = await ActEnvironmentFailureAsync(arrangeContext);

		// Assert
		AssertToolCallFailed(actResult);
		AssertCommandExitCode(actResult, 1, "unknown environment names should fail before dconf starts downloading");
		AssertIncludesErrorMessage(actResult, "failed dconf environment execution should emit error diagnostics");
		AssertFailureMentionsMissingEnvironment(actResult, arrangeContext.EnvironmentName!);
		AssertWorkspaceWasNotPopulated(arrangeContext);
	}

	[AllureStep("Arrange download-configuration-by-build MCP session")]
	private static async Task<DownloadConfigurationArrangeContext> ArrangeBuildAsync() {
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-dconf-build-e2e-{Guid.NewGuid():N}");
		string workspacePath = Path.Combine(rootDirectory, "workspace");
		string buildPath = Path.Combine(rootDirectory, "build");
		Directory.CreateDirectory(rootDirectory);
		CopyDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tpl", "workspace"), workspacePath);
		SeedWorkspaceSettings(workspacePath);
		CreateSyntheticNetCoreBuild(buildPath);

		McpE2ESettings settings = TestConfiguration.Load();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new DownloadConfigurationArrangeContext(rootDirectory, workspacePath, buildPath, session, cancellationTokenSource, null);
	}

	[AllureStep("Arrange download-configuration-by-environment MCP session")]
	private static async Task<DownloadConfigurationArrangeContext> ArrangeEnvironmentFailureAsync() {
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-dconf-env-e2e-{Guid.NewGuid():N}");
		string workspacePath = Path.Combine(rootDirectory, "workspace");
		string environmentName = $"missing-dconf-env-{Guid.NewGuid():N}";
		Directory.CreateDirectory(rootDirectory);
		CopyDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tpl", "workspace"), workspacePath);
		SeedWorkspaceSettings(workspacePath);

		McpE2ESettings settings = TestConfiguration.Load();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new DownloadConfigurationArrangeContext(rootDirectory, workspacePath, null, session, cancellationTokenSource, environmentName);
	}

	[AllureStep("Act by invoking download-configuration-by-build through MCP")]
	private static async Task<DownloadConfigurationActResult> ActBuildAsync(DownloadConfigurationArrangeContext arrangeContext) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(BuildToolName,
			because: "the build-based dconf MCP tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			BuildToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["build-path"] = arrangeContext.BuildPath,
					["workspace-path"] = arrangeContext.WorkspacePath
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new DownloadConfigurationActResult(callResult, execution);
	}

	[AllureStep("Act by invoking download-configuration-by-environment through MCP")]
	private static async Task<DownloadConfigurationActResult> ActEnvironmentFailureAsync(DownloadConfigurationArrangeContext arrangeContext) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(EnvironmentToolName,
			because: "the environment-based dconf MCP tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			EnvironmentToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = arrangeContext.EnvironmentName,
					["workspace-path"] = arrangeContext.WorkspacePath
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new DownloadConfigurationActResult(callResult, execution);
	}

	[AllureStep("Assert MCP tool call succeeded")]
	private static void AssertToolCallSucceeded(DownloadConfigurationActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "successful download-configuration execution should return a normal MCP tool result");
	}

	[AllureStep("Assert MCP tool call failed")]
	private static void AssertToolCallFailed(DownloadConfigurationActResult actResult) {
		(actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0).Should().BeTrue(
			because: "invalid environment requests should fail instead of succeeding silently");
	}

	[AllureStep("Assert command exit code")]
	private static void AssertCommandExitCode(DownloadConfigurationActResult actResult, int expectedExitCode, string because) {
		actResult.Execution.ExitCode.Should().Be(expectedExitCode, because: because);
	}

	[AllureStep("Assert execution includes Info message")]
	private static void AssertIncludesInfoMessage(DownloadConfigurationActResult actResult, string because) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "command execution should emit human-readable output");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: because);
	}

	[AllureStep("Assert execution includes Error message")]
	private static void AssertIncludesErrorMessage(DownloadConfigurationActResult actResult, string because) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: because);
	}

	[AllureStep("Assert NetCore artifacts were copied into the workspace")]
	private static void AssertNetCoreArtifactsWereCopied(DownloadConfigurationArrangeContext arrangeContext) {
		string rootAssembly = Path.Combine(arrangeContext.WorkspacePath, ".application", "net-core", "core-bin", "Terrasoft.Core.dll");
		string libAssembly = Path.Combine(arrangeContext.WorkspacePath, ".application", "net-core", "Lib", "Sample.Lib.dll");
		string configurationAssembly = Path.Combine(arrangeContext.WorkspacePath, ".application", "net-core", "bin", "Terrasoft.Configuration.dll");
		string odataAssembly = Path.Combine(arrangeContext.WorkspacePath, ".application", "net-core", "bin", "Terrasoft.Configuration.ODataEntities.dll");
		string packageAssembly = Path.Combine(arrangeContext.WorkspacePath, ".application", "net-core", "packages", "SamplePkg", "Files", "bin", "Package.dll");

		File.Exists(rootAssembly).Should().BeTrue(
			because: "download-configuration-by-build should copy root DLL files into `.application/net-core/core-bin`");
		File.Exists(libAssembly).Should().BeTrue(
			because: "download-configuration-by-build should copy `Terrasoft.Configuration/Lib` files into `.application/net-core/Lib`");
		File.Exists(configurationAssembly).Should().BeTrue(
			because: "download-configuration-by-build should copy the main configuration DLL from the latest numbered conf folder");
		File.Exists(odataAssembly).Should().BeTrue(
			because: "download-configuration-by-build should copy the OData configuration DLL from the latest numbered conf folder");
		File.Exists(packageAssembly).Should().BeTrue(
			because: "download-configuration-by-build should copy package content when `Files/bin` exists");
	}

	[AllureStep("Assert invalid environment diagnostics mention the missing environment name")]
	private static void AssertFailureMentionsMissingEnvironment(DownloadConfigurationActResult actResult, string environmentName) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "failed environment execution should explain why the call was rejected");
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(environmentName)}.*not found|an error occurred invoking '{Regex.Escape(EnvironmentToolName)}')",
			because: "the failure should either identify the missing environment directly or include the MCP invocation wrapper");
	}

	[AllureStep("Assert workspace was not populated")]
	private static void AssertWorkspaceWasNotPopulated(DownloadConfigurationArrangeContext arrangeContext) {
		string netCoreFolder = Path.Combine(arrangeContext.WorkspacePath, ".application", "net-core");
		string netFrameworkFolder = Path.Combine(arrangeContext.WorkspacePath, ".application", "net-framework");

		Directory.Exists(netCoreFolder).Should().BeFalse(
			because: "unknown environment requests should not create downloaded net-core artifacts");
		Directory.Exists(netFrameworkFolder).Should().BeFalse(
			because: "unknown environment requests should not create downloaded net-framework artifacts");
	}

	private static void CreateSyntheticNetCoreBuild(string buildPath) {
		Directory.CreateDirectory(buildPath);
		File.WriteAllText(Path.Combine(buildPath, "Terrasoft.Core.dll"), "core");

		string libPath = Path.Combine(buildPath, "Terrasoft.Configuration", "Lib");
		Directory.CreateDirectory(libPath);
		File.WriteAllText(Path.Combine(libPath, "Sample.Lib.dll"), "lib");

		string confPath = Path.Combine(buildPath, "conf", "bin", "001");
		Directory.CreateDirectory(confPath);
		File.WriteAllText(Path.Combine(confPath, "Terrasoft.Configuration.dll"), "cfg");
		File.WriteAllText(Path.Combine(confPath, "Terrasoft.Configuration.ODataEntities.dll"), "odata");

		string packageBinPath = Path.Combine(buildPath, "Terrasoft.Configuration", "Pkg", "SamplePkg", "Files", "bin");
		Directory.CreateDirectory(packageBinPath);
		File.WriteAllText(Path.Combine(packageBinPath, "Package.dll"), "pkg");
	}

	private static void SeedWorkspaceSettings(string workspacePath) {
		string workspaceSettingsPath = Path.Combine(workspacePath, ".clio", "workspaceSettings.json");
		File.WriteAllText(workspaceSettingsPath, "{}");
	}

	private static void CopyDirectory(string sourcePath, string destinationPath) {
		Directory.CreateDirectory(destinationPath);

		foreach (string directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories)) {
			string relativePath = Path.GetRelativePath(sourcePath, directory);
			Directory.CreateDirectory(Path.Combine(destinationPath, relativePath));
		}

		foreach (string file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories)) {
			string relativePath = Path.GetRelativePath(sourcePath, file);
			string destinationFilePath = Path.Combine(destinationPath, relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
			File.Copy(file, destinationFilePath, overwrite: true);
		}
	}

	private sealed record DownloadConfigurationArrangeContext(
		string RootDirectory,
		string WorkspacePath,
		string? BuildPath,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource,
		string? EnvironmentName) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();

			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
		}
	}

	private sealed record DownloadConfigurationActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
