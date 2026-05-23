using System.Text.Json;
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
/// End-to-end tests for the workspace sync MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("workspace-sync")]
[NonParallelizable]
public sealed class WorkspaceSyncToolE2ETests {
	private const string PushToolName = PushWorkspaceTool.PushWorkspaceToolName;
	private const string RestoreToolName = RestoreWorkspaceTool.RestoreWorkspaceToolName;
	private const string PackageListToolName = GetPkgListTool.GetPkgListToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes push-workspace with an unknown environment name, and verifies that the tool reports a readable failure without mutating the workspace directory.")]
	[AllureTag(PushToolName)]
	[AllureName("Push workspace reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call push-workspace with a guaranteed-missing environment name and verifies the error diagnostics plus the absence of workspace mutations.")]
	public async Task PushWorkspace_Should_Report_Invalid_Environment() {
		// Arrange
		await using WorkspaceSyncArrangeContext arrangeContext = await ArrangeInvalidEnvironmentAsync("pushw");

		// Act
		WorkspaceCommandActResult actResult = await ActWorkspaceCommandAsync(arrangeContext, PushToolName, arrangeContext.WorkspacePath);

		// Assert
		AssertToolCallFailed(actResult);
		AssertCommandExitCode(actResult, 1, "unknown environment names should fail before push-workspace starts operating on the workspace");
		AssertIncludesErrorMessage(actResult, "failed push-workspace execution should emit error diagnostics");
		AssertFailureMentionsMissingEnvironment(actResult, arrangeContext.EnvironmentName, PushToolName);
		AssertWorkspaceWasNotMutated(arrangeContext.WorkspacePath);
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes restore-workspace with an unknown environment name, and verifies that the tool reports a readable failure without mutating the workspace directory.")]
	[AllureTag(RestoreToolName)]
	[AllureName("Restore workspace reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call restore-workspace with a guaranteed-missing environment name and verifies the error diagnostics plus the absence of workspace mutations.")]
	public async Task RestoreWorkspace_Should_Report_Invalid_Environment() {
		// Arrange
		await using WorkspaceSyncArrangeContext arrangeContext = await ArrangeInvalidEnvironmentAsync("restorew");

		// Act
		WorkspaceCommandActResult actResult = await ActWorkspaceCommandAsync(arrangeContext, RestoreToolName, arrangeContext.WorkspacePath);

		// Assert
		AssertToolCallFailed(actResult);
		AssertCommandExitCode(actResult, 1, "unknown environment names should fail before restore-workspace starts downloading or creating workspace content");
		AssertIncludesErrorMessage(actResult, "failed restore-workspace execution should emit error diagnostics");
		AssertFailureMentionsMissingEnvironment(actResult, arrangeContext.EnvironmentName, RestoreToolName);
		AssertWorkspaceWasNotMutated(arrangeContext.WorkspacePath);
	}

	[Test]
	[Description("Creates a workspace and package with the real clio CLI, pushes it through MCP, and verifies the package appears in the target environment through list-packages.")]
	[AllureTag(PushToolName)]
	[AllureTag(PackageListToolName)]
	[AllureName("Push workspace publishes the arranged package and list-packages returns it")]
	[AllureDescription("Uses the real clio CLI to create a workspace and package locally, invokes push-workspace through MCP, then verifies the pushed package is returned by list-packages with matching version and maintainer.")]
	public async Task PushWorkspace_Should_Publish_Arranged_Package_And_GetPkgList_Should_Return_It() {
		// Arrange
		await using WorkspaceSyncArrangeContext arrangeContext = await ArrangeSandboxWorkspaceAsync();

		// Act
		WorkspaceCommandActResult pushResult = await ActWorkspaceCommandAsync(arrangeContext, PushToolName, arrangeContext.WorkspacePath);
		PackageListActResult packageListResult = await ActGetPkgListAsync(arrangeContext, arrangeContext.PackageName);

		// Assert
		AssertToolCallSucceeded(pushResult);
		AssertCommandExitCode(pushResult, 0, "push-workspace should succeed for a valid sandbox workspace and environment");
		AssertIncludesInfoMessage(pushResult, "successful push-workspace execution should emit progress output");
		AssertPackageWasPublished(packageListResult, arrangeContext.PackageMetadata);
	}

	[Test]
	[Description("Creates a workspace and package with the real clio CLI, pushes it through MCP, deletes the local package files, restores into the same workspace, and verifies the package metadata is recreated from the environment.")]
	[AllureTag(PushToolName)]
	[AllureTag(RestoreToolName)]
	[AllureName("Restore workspace recreates the pushed package in the same workspace")]
	[AllureDescription("Uses the real clio CLI to create a workspace and package locally, pushes it through MCP, removes the local package folder, restores through MCP into the same workspace, and verifies the restored package descriptor matches the pushed package.")]
	public async Task RestoreWorkspace_Should_Recreate_Pushed_Package_In_Same_Workspace() {
		// Arrange
		await using WorkspaceSyncArrangeContext arrangeContext = await ArrangeSandboxWorkspaceAsync();
		string descriptorPath = FindPackageDescriptorPath(arrangeContext.WorkspacePath, arrangeContext.PackageName);
		string packageDirectoryPath = Directory.GetParent(descriptorPath)!.FullName;

		// Act
		WorkspaceCommandActResult pushResult = await ActWorkspaceCommandAsync(arrangeContext, PushToolName, arrangeContext.WorkspacePath);
		DeletePackageDirectory(packageDirectoryPath);
		Directory.Exists(packageDirectoryPath).Should().BeFalse(
			because: "the local package directory should be removed before restore-workspace runs so the restore side effect is observable");
		WorkspaceCommandActResult restoreResult = await ActWorkspaceCommandAsync(arrangeContext, RestoreToolName, arrangeContext.WorkspacePath);

		// Assert
		AssertToolCallSucceeded(pushResult);
		AssertCommandExitCode(pushResult, 0, "restore coverage depends on push-workspace completing successfully first");
		AssertIncludesInfoMessage(pushResult, "successful push-workspace execution should emit progress output");
		AssertToolCallSucceeded(restoreResult);
		AssertCommandExitCode(restoreResult, 0, "restore-workspace should succeed for a valid sandbox environment and existing workspace settings");
		AssertIncludesInfoMessage(restoreResult, "successful restore-workspace execution should emit progress output");
		AssertRestoredPackageMatchesSourceMetadata(arrangeContext.WorkspacePath, arrangeContext.PackageName, arrangeContext.PackageMetadata);
	}

	[Test]
	[Description("Starts the real clio MCP server, lists tools, and verifies that both workspace-sync MCP endpoints are advertised as destructive.")]
	[AllureTag(PushToolName)]
	[AllureTag(RestoreToolName)]
	[AllureName("Workspace sync tools advertise destructive metadata")]
	[AllureDescription("Uses the real clio MCP server tool discovery response to verify that push-workspace and restore-workspace expose the destructive hint required for client-side safety policies.")]
	public async Task WorkspaceSync_Tools_Should_Be_Advertised_As_Destructive() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);

		// Assert
		AssertToolIsAdvertisedAsDestructive(tools, PushToolName);
		AssertToolIsAdvertisedAsDestructive(tools, RestoreToolName);
	}

	[Test]
	[Description("Starts the real clio MCP server, inspects push-workspace tool discovery metadata, and verifies that skip-backup is exposed as an optional input argument.")]
	[AllureTag(PushToolName)]
	[AllureName("Push workspace advertises optional skip-backup argument")]
	[AllureDescription("Uses the real clio MCP server tool discovery response to verify that push-workspace exposes the optional skip-backup argument for callers that explicitly want to skip the pre-install backup.")]
	public async Task PushWorkspace_Tool_Should_Advertise_Optional_SkipBackup_Argument() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		using CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		await using McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);

		// Act
		IList<McpClientTool> tools = await session.ListToolsAsync(cancellationTokenSource.Token);

		// Assert
		AssertPushWorkspaceToolAdvertisesSkipBackup(tools);
	}

	[AllureStep("Arrange workspace-sync invalid-environment MCP session")]
	private static async Task<WorkspaceSyncArrangeContext> ArrangeInvalidEnvironmentAsync(string toolPrefix) {
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-{toolPrefix}-mcp-e2e-{Guid.NewGuid():N}");
		string workspacePath = Path.Combine(rootDirectory, "workspace");
		string restoreWorkspacePath = Path.Combine(rootDirectory, "restore-workspace");
		string environmentName = $"missing-{toolPrefix}-env-{Guid.NewGuid():N}";
		Directory.CreateDirectory(workspacePath);

		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new WorkspaceSyncArrangeContext(
			settings,
			rootDirectory,
			workspacePath,
			WorkspaceName: "workspace",
			restoreWorkspacePath,
			RestoreWorkspaceName: "restore-workspace",
			environmentName,
			PackageName: string.Empty,
			PackageMetadata: null,
			session,
			cancellationTokenSource);
	}

	[AllureStep("Arrange workspace-sync sandbox lifecycle")]
	private static async Task<WorkspaceSyncArrangeContext> ArrangeSandboxWorkspaceAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run destructive MCP end-to-end tests.");
		}

		TestConfiguration.EnsureSandboxIsConfigured(settings);
		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-workspace-sync-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootDirectory);

		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		string restoreWorkspaceName = $"restore-{Guid.NewGuid():N}";
		string restoreWorkspacePath = Path.Combine(rootDirectory, restoreWorkspaceName);
		string packageName = $"Pkg{Guid.NewGuid():N}".Substring(0, 18);
		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));

		await ClioCliCommandRunner.EnsureCliogateInstalledAsync(
			settings,
			settings.Sandbox.EnvironmentName!,
			cancellationTokenSource.Token);
		await CreateEmptyWorkspaceAsync(settings, rootDirectory, workspaceName, cancellationTokenSource.Token);
		await AddPackageAsync(settings, workspacePath, packageName, cancellationTokenSource.Token);
		PackageMetadata packageMetadata = ReadPackageMetadata(workspacePath, packageName);

		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new WorkspaceSyncArrangeContext(
			settings,
			rootDirectory,
			workspacePath,
			workspaceName,
			restoreWorkspacePath,
			restoreWorkspaceName,
			settings.Sandbox.EnvironmentName!,
			packageName,
			packageMetadata,
			session,
			cancellationTokenSource);
	}

	[AllureStep("Act by invoking workspace-sync tool through MCP")]
	private static async Task<WorkspaceCommandActResult> ActWorkspaceCommandAsync(
		WorkspaceSyncArrangeContext arrangeContext,
		string toolName,
		string workspacePath) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(toolName,
			because: "the requested workspace-sync MCP tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			toolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = arrangeContext.EnvironmentName,
					["workspace-path"] = workspacePath
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new WorkspaceCommandActResult(callResult, execution);
	}

	[AllureStep("Act by invoking list-packages through MCP")]
	private static async Task<PackageListActResult> ActGetPkgListAsync(
		WorkspaceSyncArrangeContext arrangeContext,
		string filter) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(PackageListToolName,
			because: "the list-packages MCP tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			PackageListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = arrangeContext.EnvironmentName,
					["filter"] = filter
				}
			},
			arrangeContext.CancellationTokenSource.Token);

		IReadOnlyList<GetPkgListEnvelope> packages = GetPkgListResultParser.Extract(callResult);
		return new PackageListActResult(callResult, packages);
	}

	[AllureStep("Assert MCP tool call failed")]
	private static void AssertToolCallFailed(WorkspaceCommandActResult actResult) {
		(actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0).Should().BeTrue(
			because: "invalid environment requests should fail instead of succeeding silently");
	}

	[AllureStep("Assert MCP tool call succeeded")]
	private static void AssertToolCallSucceeded(WorkspaceCommandActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "successful workspace-sync requests should return a normal MCP tool result");
	}

	[AllureStep("Assert command exit code")]
	private static void AssertCommandExitCode(WorkspaceCommandActResult actResult, int expectedExitCode, string because) {
		actResult.Execution.ExitCode.Should().Be(expectedExitCode, because: because);
	}

	[AllureStep("Assert execution includes Error message")]
	private static void AssertIncludesErrorMessage(WorkspaceCommandActResult actResult, string because) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == LogDecoratorType.Error,
			because: because);
	}

	[AllureStep("Assert execution includes Info message")]
	private static void AssertIncludesInfoMessage(WorkspaceCommandActResult actResult, string because) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "successful command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(message => message.MessageType == LogDecoratorType.Info,
			because: because);
	}

	[AllureStep("Assert invalid environment diagnostics mention the missing environment name")]
	private static void AssertFailureMentionsMissingEnvironment(
		WorkspaceCommandActResult actResult,
		string environmentName,
		string toolName) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "failed workspace-sync execution should explain why the call was rejected");
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(environmentName)}.*not found|environment.*not.*found|{Regex.Escape(toolName)}|error occurred invoking)",
			because: "the failure should either identify the missing environment directly or include the MCP invocation wrapper");
	}

	[AllureStep("Assert workspace was not mutated")]
	private static void AssertWorkspaceWasNotMutated(string workspacePath) {
		Directory.EnumerateFileSystemEntries(workspacePath).Should().BeEmpty(
			because: "invalid environment requests must not create or modify files in the target workspace directory");
	}

	[AllureStep("Assert discovered MCP tool is marked as destructive")]
	private static void AssertToolIsAdvertisedAsDestructive(IList<McpClientTool> tools, string toolName) {
		McpClientTool tool = tools.Single(tool => tool.Name == toolName);

		tool.ProtocolTool.Annotations.Should().NotBeNull(
			because: "the MCP server should expose tool annotations for clients that apply safety policies");
		tool.ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue(
			because: "workspace-sync tools mutate local workspace state and/or the target environment");
	}

	[AllureStep("Assert push-workspace MCP schema advertises optional skip-backup argument")]
	private static void AssertPushWorkspaceToolAdvertisesSkipBackup(IList<McpClientTool> tools) {
		McpClientTool tool = tools.Single(tool => tool.Name == PushToolName);
		JsonElement inputSchema = JsonSerializer.SerializeToElement(tool.ProtocolTool.InputSchema);
		JsonElement argsSchema = inputSchema.GetProperty("properties").GetProperty("args");
		JsonElement argsProperties = argsSchema.GetProperty("properties");
		string[] requiredArgs = argsSchema.GetProperty("required").EnumerateArray()
			.Select(item => item.GetString()!)
			.ToArray();

		argsProperties.TryGetProperty("skip-backup", out JsonElement skipBackupProperty).Should().BeTrue(
			because: "push-workspace callers need an explicit way to disable backup without changing the default behavior");
		skipBackupProperty.GetProperty("type").GetString().Should().Be("boolean",
			because: "skip-backup should be modeled as a boolean MCP input");
		requiredArgs.Should().NotContain("skip-backup",
			because: "backup skipping must remain opt-in and the default behavior should still create backups when the argument is omitted");
	}

	[AllureStep("Assert list-packages returns the pushed package")]
	private static void AssertPackageWasPublished(PackageListActResult actResult, PackageMetadata? expectedPackage) {
		expectedPackage.Should().NotBeNull(
			because: "the sandbox arrange step should capture package metadata before push-workspace runs");
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "list-packages should return a structured MCP payload after a successful push");
		actResult.Packages.Should().ContainSingle(
			because: "the filtered list-packages request should return the unique package created for the test");
		GetPkgListEnvelope package = actResult.Packages.Single();
		package.Name.Should().Be(expectedPackage!.Name,
			because: "the package returned by list-packages should match the package that was pushed");
		package.Version.Should().Be(expectedPackage.Version,
			because: "the version returned by list-packages should match the descriptor version from the arranged workspace package");
		package.Maintainer.Should().Be(expectedPackage.Maintainer,
			because: "the maintainer returned by list-packages should match the descriptor maintainer from the arranged workspace package");
	}

	[AllureStep("Assert restored workspace contains the pushed package metadata")]
	private static void AssertRestoredPackageMatchesSourceMetadata(
		string workspacePath,
		string packageName,
		PackageMetadata? expectedPackage) {
		expectedPackage.Should().NotBeNull(
			because: "restore assertions depend on the arranged source package metadata");
		string descriptorPath = FindPackageDescriptorPath(workspacePath, packageName);
		File.Exists(descriptorPath).Should().BeTrue(
			because: "restore-workspace should recreate the pushed package descriptor inside the existing workspace");

		PackageMetadata restoredPackage = ReadPackageMetadataFromDescriptor(descriptorPath);
		restoredPackage.Name.Should().Be(expectedPackage!.Name,
			because: "restore-workspace should recreate the same package name that was previously pushed");
		restoredPackage.Version.Should().Be(expectedPackage.Version,
			because: "restore-workspace should preserve the package version from the environment");
		restoredPackage.Maintainer.Should().Be(expectedPackage.Maintainer,
			because: "restore-workspace should preserve the package maintainer from the environment");
	}

	private static void DeletePackageDirectory(string packageDirectoryPath) {
		Directory.Exists(packageDirectoryPath).Should().BeTrue(
			because: "the arranged workspace package directory should exist before the restore test deletes it");
		Directory.Delete(packageDirectoryPath, recursive: true);
	}

	private static async Task CreateEmptyWorkspaceAsync(
		McpE2ESettings settings,
		string rootDirectory,
		string workspaceName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["create-workspace", workspaceName, "--empty", "--directory", rootDirectory],
			workingDirectory: rootDirectory,
			cancellationToken: cancellationToken);
	}

	private static async Task AddPackageAsync(
		McpE2ESettings settings,
		string workspacePath,
		string packageName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["add-package", packageName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
	}

	private static PackageMetadata ReadPackageMetadata(string workspacePath, string packageName) {
		string descriptorPath = FindPackageDescriptorPath(workspacePath, packageName);
		return ReadPackageMetadataFromDescriptor(descriptorPath);
	}

	private static string FindPackageDescriptorPath(string workspacePath, string packageName) {
		string? descriptorPath = Directory
			.GetFiles(workspacePath, "descriptor.json", SearchOption.AllDirectories)
			.FirstOrDefault(path =>
				string.Equals(
					new DirectoryInfo(Path.GetDirectoryName(path)!).Name,
					packageName,
					StringComparison.OrdinalIgnoreCase));

		descriptorPath.Should().NotBeNullOrWhiteSpace(
			because: "the arranged workspace package should contain a descriptor.json file under its package directory");

		return descriptorPath!;
	}

	private static PackageMetadata ReadPackageMetadataFromDescriptor(string descriptorPath) {
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(descriptorPath));
		JsonElement descriptor = document.RootElement.GetProperty("Descriptor");
		return new PackageMetadata(
			descriptor.GetProperty("Name").GetString() ?? string.Empty,
			descriptor.GetProperty("PackageVersion").GetString() ?? string.Empty,
			descriptor.GetProperty("Maintainer").GetString() ?? string.Empty);
	}

	private sealed record WorkspaceSyncArrangeContext(
		McpE2ESettings Settings,
		string RootDirectory,
		string WorkspacePath,
		string WorkspaceName,
		string RestoreWorkspacePath,
		string RestoreWorkspaceName,
		string EnvironmentName,
		string PackageName,
		PackageMetadata? PackageMetadata,
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();

			if (Directory.Exists(RootDirectory)) {
				Directory.Delete(RootDirectory, recursive: true);
			}
		}
	}

	private sealed record WorkspaceCommandActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);

	private sealed record PackageListActResult(
		CallToolResult CallResult,
		IReadOnlyList<GetPkgListEnvelope> Packages);

	private sealed record PackageMetadata(
		string Name,
		string Version,
		string Maintainer);
}
