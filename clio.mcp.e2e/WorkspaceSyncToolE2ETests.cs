using System.Text.Json;
using System.Text.RegularExpressions;
using Allure.Net.Commons;
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

	// ENG-92459: the two restore tests differ only in their assertions; both need a package that was
	// already pushed to the environment so restore-workspace has something to recreate. Provision and push
	// that package once for the fixture (lazily, so the destructive opt-in / sandbox guards still skip the
	// restore tests when the stand is unavailable) instead of pushing a fresh package per restore test. The
	// fixture is [NonParallelizable], so the lazy init runs without a race; each restore test deletes its
	// local copy and restores independently, so they remain order-independent.
	private string? _sharedRootDirectory;
	private string? _sharedWorkspacePath;
	private string? _sharedPackageName;
	private string? _sharedEnvironmentName;
	private PackageMetadata? _sharedPackageMetadata;

	[OneTimeTearDown]
	public void CleanupSharedRestoreWorkspace() {
		if (_sharedRootDirectory is not null && Directory.Exists(_sharedRootDirectory)) {
			Directory.Delete(_sharedRootDirectory, recursive: true);
		}
	}

	[Category("McpE2E.Sandbox")]
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

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Creates a workspace and package with the real clio CLI, pushes it through MCP, deletes the local package files, restores into the same workspace, and verifies the package metadata is recreated from the environment.")]
	[AllureTag(PushToolName)]
	[AllureTag(RestoreToolName)]
	[AllureName("Restore workspace recreates the pushed package in the same workspace")]
	[AllureDescription("Uses the real clio CLI to create a workspace and package locally, pushes it through MCP, removes the local package folder, restores through MCP into the same workspace, and verifies the restored package descriptor matches the pushed package.")]
	public async Task RestoreWorkspace_Should_Recreate_Pushed_Package_In_Same_Workspace() {
		// Arrange - the package is already pushed once for the fixture (ENG-92459); push assertions are
		// covered by PushWorkspace_Should_Publish_Arranged_Package_And_GetPkgList_Should_Return_It.
		await using WorkspaceSyncArrangeContext arrangeContext = await ArrangeSharedRestoreWorkspaceAsync();
		string descriptorPath = FindPackageDescriptorPath(arrangeContext.WorkspacePath, arrangeContext.PackageName);
		string packageDirectoryPath = Directory.GetParent(descriptorPath)!.FullName;

		// Act
		DeletePackageDirectory(packageDirectoryPath);
		Directory.Exists(packageDirectoryPath).Should().BeFalse(
			because: "the local package directory should be removed before restore-workspace runs so the restore side effect is observable");
		WorkspaceCommandActResult restoreResult = await ActWorkspaceCommandAsync(arrangeContext, RestoreToolName, arrangeContext.WorkspacePath);

		// Assert
		AssertToolCallSucceeded(restoreResult);
		AssertCommandExitCode(restoreResult, 0, "restore-workspace should succeed for a valid sandbox environment and existing workspace settings");
		AssertIncludesInfoMessage(restoreResult, "successful restore-workspace execution should emit progress output");
		AssertRestoredPackageMatchesSourceMetadata(arrangeContext.WorkspacePath, arrangeContext.PackageName, arrangeContext.PackageMetadata);
	}

	[Category("McpE2E.Sandbox")]
	[Test]
	[Description("Drives restore-workspace (a [RequiresPackage(\"cliogate\")] command) through the real clio MCP server against a sandbox where cliogate IS installed, and verifies the environment-scoped package-requirement gate does NOT false-positive: the tool runs to completion instead of refusing. "
		+ "Residual gap: the 'package absent' refusal branch is NOT covered here because the sandbox arrange step (ArrangeSandboxWorkspaceAsync -> EnsureCliogateInstalledAsync) guarantees cliogate is present, and the invalid-environment tests fail during command resolution BEFORE the gate runs. Asserting a refusal would require a live environment that lacks cliogate, which the current harness cannot provision. The refusal branch is covered at the unit level in clio.tests/Command/McpServer/BaseToolTests.cs.")]
	[AllureTag(RestoreToolName)]
	[AllureName("Restore workspace package-requirement gate does not false-positive when cliogate is installed")]
	[AllureDescription("Uses the real clio MCP server to restore a previously pushed package into a sandbox where cliogate is installed, proving the environment-scoped [RequiresPackage] gate lets the command through rather than refusing. The 'package absent' refusal branch is documented as a residual harness gap and covered by unit tests.")]
	public async Task RestoreWorkspace_PackageRequirementGate_Should_Not_Refuse_When_Cliogate_Is_Installed() {
		// Arrange - reuse the package pushed once for the fixture (ENG-92459); this test only exercises the
		// restore-workspace [RequiresPackage] gate, so the push assertions live in the dedicated push test.
		await using WorkspaceSyncArrangeContext arrangeContext = await ArrangeSharedRestoreWorkspaceAsync();
		string descriptorPath = FindPackageDescriptorPath(arrangeContext.WorkspacePath, arrangeContext.PackageName);
		string packageDirectoryPath = Directory.GetParent(descriptorPath)!.FullName;

		// Act
		DeletePackageDirectory(packageDirectoryPath);
		WorkspaceCommandActResult restoreResult = await ActWorkspaceCommandAsync(arrangeContext, RestoreToolName, arrangeContext.WorkspacePath);

		// Assert
		AssertToolCallSucceeded(restoreResult);
		AssertCommandExitCode(restoreResult, 0,
			"with cliogate installed the [RequiresPackage] gate must let restore-workspace through instead of refusing");
		AssertGateDidNotRefuse(restoreResult, "cliogate");
	}

	private static async Task<WorkspaceSyncArrangeContext> ArrangeInvalidEnvironmentAsync(string toolPrefix) {
		return await AllureApi.Step("Arrange workspace-sync invalid-environment MCP session", async () => {
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
				cancellationTokenSource,
				OwnsRootDirectory: true);
		});
	}

	private static async Task<WorkspaceSyncArrangeContext> ArrangeSandboxWorkspaceAsync() {
		return await AllureApi.Step("Arrange workspace-sync sandbox lifecycle", async () => {
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
				cancellationTokenSource,
				OwnsRootDirectory: true);
		});
	}

	private async Task<WorkspaceSyncArrangeContext> ArrangeSharedRestoreWorkspaceAsync() {
		return await AllureApi.Step("Arrange shared workspace-sync restore lifecycle", async () => {
			McpE2ESettings settings = TestConfiguration.Load();
			settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
			if (!settings.AllowDestructiveMcpTests) {
				Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run destructive MCP end-to-end tests.");
			}

			TestConfiguration.EnsureSandboxIsConfigured(settings);
			CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(5));
			await EnsureSharedRestoreWorkspaceAsync(settings, cancellationTokenSource.Token);

			McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
			return new WorkspaceSyncArrangeContext(
				settings,
				_sharedRootDirectory!,
				_sharedWorkspacePath!,
				Path.GetFileName(_sharedWorkspacePath!),
				RestoreWorkspacePath: string.Empty,
				RestoreWorkspaceName: string.Empty,
				_sharedEnvironmentName!,
				_sharedPackageName!,
				_sharedPackageMetadata,
				session,
				cancellationTokenSource,
				OwnsRootDirectory: false);
		});
	}

	/// <summary>
	/// Lazily provisions a single workspace + package that is created, has its package added and is pushed
	/// to the sandbox environment once (ENG-92459), so the restore tests can recreate it without each
	/// re-pushing a fresh package. Subsequent calls reuse the cached workspace; relies on the fixture being
	/// [NonParallelizable].
	/// </summary>
	private async Task EnsureSharedRestoreWorkspaceAsync(McpE2ESettings settings, CancellationToken cancellationToken) {
		if (_sharedPackageName is not null) {
			return;
		}

		string environmentName = settings.Sandbox.EnvironmentName!;
		await ClioCliCommandRunner.EnsureCliogateInstalledAsync(settings, environmentName, cancellationToken);

		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-workspace-sync-e2e-{Guid.NewGuid():N}");
		Directory.CreateDirectory(rootDirectory);
		string workspaceName = $"workspace-{Guid.NewGuid():N}";
		string workspacePath = Path.Combine(rootDirectory, workspaceName);
		string packageName = $"Pkg{Guid.NewGuid():N}".Substring(0, 18);

		await CreateEmptyWorkspaceAsync(settings, rootDirectory, workspaceName, cancellationToken);
		await AddPackageAsync(settings, workspacePath, packageName, cancellationToken);
		PackageMetadata packageMetadata = ReadPackageMetadata(workspacePath, packageName);
		await PushWorkspaceCliAsync(settings, workspacePath, environmentName, cancellationToken);

		_sharedRootDirectory = rootDirectory;
		_sharedWorkspacePath = workspacePath;
		_sharedPackageName = packageName;
		_sharedEnvironmentName = environmentName;
		_sharedPackageMetadata = packageMetadata;
	}

	private static async Task PushWorkspaceCliAsync(
		McpE2ESettings settings,
		string workspacePath,
		string environmentName,
		CancellationToken cancellationToken) {
		await ClioCliCommandRunner.RunAndAssertSuccessAsync(
			settings,
			["push-workspace", "-e", environmentName],
			workingDirectory: workspacePath,
			cancellationToken: cancellationToken);
	}

	private static async Task<WorkspaceCommandActResult> ActWorkspaceCommandAsync(
		WorkspaceSyncArrangeContext arrangeContext,
		string toolName,
		string workspacePath) {
		return await AllureApi.Step("Act by invoking workspace-sync tool through MCP", async () => {
			IReadOnlyCollection<string> toolNames =
				await arrangeContext.Session.ListReachableToolNamesAsync(arrangeContext.CancellationTokenSource.Token);
			toolNames.Should().Contain(toolName,
				because: "the requested workspace-sync MCP tool must be discoverable via the get-tool-contract compact index before the end-to-end call can be executed");

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
		});
	}

	private static async Task<PackageListActResult> ActGetPkgListAsync(
		WorkspaceSyncArrangeContext arrangeContext,
		string filter) {
		return await AllureApi.Step("Act by invoking list-packages through MCP", async () => {
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
		});
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

	[AllureStep("Assert the package-requirement gate did not refuse the call")]
	private static void AssertGateDidNotRefuse(WorkspaceCommandActResult actResult, string packageName) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? []).Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotMatchRegex(
			$"(?is)to use this command, you need to install the {Regex.Escape(packageName)} package",
			because: "with the required package installed the [RequiresPackage] gate must not emit its refusal message");
		combinedOutput.Should().NotContain("Could not verify package requirements",
			because: "with a reachable environment the gate must verify requirements cleanly rather than surfacing a verification failure");
	}

	// The destructive-metadata and skip-backup-schema advertisement asserts moved to
	// WorkspaceSyncContractToolE2ETests, where they read the get-tool-contract compact index / full
	// contract (the lazy-surface replacement for tools/list annotations and input schemas). The stale
	// tools/list-based helper copies that previously lived here were unreferenced and were removed.

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
		CancellationTokenSource CancellationTokenSource,
		bool OwnsRootDirectory) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();

			// Restore-test contexts reuse the shared fixture workspace (see EnsureSharedRestoreWorkspaceAsync),
			// which is deleted once in [OneTimeTearDown]; only the per-test throwaway roots are owned here.
			if (OwnsRootDirectory && Directory.Exists(RootDirectory)) {
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
