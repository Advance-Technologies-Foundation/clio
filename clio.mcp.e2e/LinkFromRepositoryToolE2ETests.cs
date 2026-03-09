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
using System.Text.RegularExpressions;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the link-from-repository MCP tools.
/// </summary>
[TestFixture]
[AllureNUnit]
[AllureFeature("link-from-repository")]
[NonParallelizable]
public sealed class LinkFromRepositoryToolE2ETests {
	private const string EnvironmentToolName = LinkFromRepositoryTool.LinkFromRepositoryByEnvironmentToolName;
	private const string EnvPkgPathToolName = LinkFromRepositoryTool.LinkFromRepositoryByEnvPackagePathToolName;

	[Test]
	[Description("Starts the real clio MCP server, invokes the direct-path link-from-repository tool against a temporary repository and Creatio package folder, and verifies the package directory becomes a symbolic link.")]
	[AllureTag(EnvPkgPathToolName)]
	[AllureName("Link From Repository tool links package by explicit environment package path")]
	[AllureDescription("Uses the real clio MCP server to link a repository package into a temporary Creatio package folder and verifies the resulting symbolic link points to the repository package content.")]
	public async Task LinkFromRepositoryByEnvPackagePath_Should_Create_Symbolic_Link() {
		// Arrange
		await using LinkFromRepositoryArrangeContext arrangeContext = await ArrangeAsync();
		EnsureSymbolicLinksAreSupported(arrangeContext);

		// Act
		LinkFromRepositoryActResult actResult = await ActByEnvPackagePathAsync(arrangeContext, "PkgA");

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult);
		AssertSuccessIncludesInfoMessage(actResult);
		AssertPackageFolderWasReplacedWithSymbolicLink(arrangeContext);
		AssertSymbolicLinkTargetsRepositoryPackage(arrangeContext, "PkgA");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes the direct-path link-from-repository tool with a comma-separated package list, and verifies that each requested package directory becomes a symbolic link.")]
	[AllureTag(EnvPkgPathToolName)]
	[AllureName("Link From Repository tool links multiple requested packages")]
	[AllureDescription("Uses the real clio MCP server to link two repository packages through a comma-separated selector and verifies both target package directories become symbolic links to repository content.")]
	public async Task LinkFromRepositoryByEnvPackagePath_Should_Link_Multiple_Packages() {
		// Arrange
		await using LinkFromRepositoryArrangeContext arrangeContext = await ArrangeAsync();
		EnsureSymbolicLinksAreSupported(arrangeContext);

		// Act
		LinkFromRepositoryActResult actResult = await ActByEnvPackagePathAsync(arrangeContext, "PkgA,PkgB");

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult);
		AssertSuccessIncludesInfoMessage(actResult);
		AssertPackageFolderWasReplacedWithSymbolicLink(arrangeContext, "PkgA");
		AssertPackageFolderWasReplacedWithSymbolicLink(arrangeContext, "PkgB");
		AssertSymbolicLinkTargetsRepositoryPackage(arrangeContext, "PkgA");
		AssertSymbolicLinkTargetsRepositoryPackage(arrangeContext, "PkgB");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes the direct-path link-from-repository tool with the wildcard selector, and verifies that all repository packages are linked into the target package directory.")]
	[AllureTag(EnvPkgPathToolName)]
	[AllureName("Link From Repository tool links all packages with wildcard selector")]
	[AllureDescription("Uses the real clio MCP server to link all repository packages with `*` and verifies that each seeded package directory becomes a symbolic link to repository content.")]
	public async Task LinkFromRepositoryByEnvPackagePath_Should_Link_All_Packages_When_Using_Wildcard() {
		// Arrange
		await using LinkFromRepositoryArrangeContext arrangeContext = await ArrangeAsync();
		EnsureSymbolicLinksAreSupported(arrangeContext);

		// Act
		LinkFromRepositoryActResult actResult = await ActByEnvPackagePathAsync(arrangeContext, "*");

		// Assert
		AssertToolCallSucceeded(actResult);
		AssertCommandExitCode(actResult);
		AssertSuccessIncludesInfoMessage(actResult);
		AssertPackageFolderWasReplacedWithSymbolicLink(arrangeContext, "PkgA");
		AssertPackageFolderWasReplacedWithSymbolicLink(arrangeContext, "PkgB");
		AssertSymbolicLinkTargetsRepositoryPackage(arrangeContext, "PkgA");
		AssertSymbolicLinkTargetsRepositoryPackage(arrangeContext, "PkgB");
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes the environment-name link-from-repository tool with an invalid environment key, and verifies that the result reports failure with human-readable diagnostics.")]
	[AllureTag(EnvironmentToolName)]
	[AllureName("Link From Repository tool reports invalid environment failures")]
	[AllureDescription("Uses the real clio MCP server to call the environment-name link-from-repository tool with a non-existent environment key and verifies failure diagnostics.")]
	public async Task LinkFromRepositoryByEnvironment_Should_Report_Failure_When_Environment_Is_Invalid() {
		// Arrange
		await using LinkFromRepositoryArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		LinkFromRepositoryActResult actResult = await ActByEnvironmentAsync(
			arrangeContext,
			$"missing-env-{Guid.NewGuid():N}",
			"PkgA");

		// Assert
		AssertToolCallFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		AssertEnvironmentFailureMentionsPlatformOrMissingEnvironment(actResult);
	}

	[Test]
	[Description("Starts the real clio MCP server, invokes the direct-path link-from-repository tool with a package name that does not exist in the temporary repository, and verifies that the result reports failure without creating a link.")]
	[AllureTag(EnvPkgPathToolName)]
	[AllureName("Link From Repository tool reports missing repository package failures")]
	[AllureDescription("Uses the real clio MCP server to call the direct-path link-from-repository tool with an unknown package name and verifies failure diagnostics plus absence of the linked package directory.")]
	public async Task LinkFromRepositoryByEnvPackagePath_Should_Report_Failure_When_Package_Is_Missing() {
		// Arrange
		await using LinkFromRepositoryArrangeContext arrangeContext = await ArrangeAsync();

		// Act
		LinkFromRepositoryActResult actResult = await ActByEnvPackagePathAsync(arrangeContext, "MissingPkg");

		// Assert
		AssertToolCallFailed(actResult);
		AssertFailureIncludesErrorMessage(actResult);
		AssertMissingPackageFailureMentionsPackageName(actResult, "MissingPkg");
		AssertPackageFolderWasNotCreated(arrangeContext, "MissingPkg");
	}

	[AllureStep("Arrange link-from-repository MCP sandbox")]
	[AllureDescription("Arrange by creating temporary repository and Creatio package directories, seeding a package folder, and starting a real clio MCP server session")]
	private static async Task<LinkFromRepositoryArrangeContext> ArrangeAsync() {
		McpE2ESettings settings = TestConfiguration.Load();
		if (!settings.AllowDestructiveMcpTests) {
			Assert.Ignore("Set McpE2E:AllowDestructiveMcpTests=true to run destructive MCP end-to-end tests.");
		}

		string rootDirectory = Path.Combine(Path.GetTempPath(), $"clio-link-from-repository-e2e-{Guid.NewGuid():N}");
		string environmentPackagesPath = Path.Combine(rootDirectory, "environment", "Pkg");
		string repositoryRootPath = Path.Combine(rootDirectory, "repository");
		string repositoryPackagesPath = Path.Combine(repositoryRootPath, "packages");
		string repositoryPackagePath = Path.Combine(repositoryPackagesPath, "PkgA");
		string secondRepositoryPackagePath = Path.Combine(repositoryPackagesPath, "PkgB");
		string environmentPackagePath = Path.Combine(environmentPackagesPath, "PkgA");
		string secondEnvironmentPackagePath = Path.Combine(environmentPackagesPath, "PkgB");

		Directory.CreateDirectory(environmentPackagesPath);
		Directory.CreateDirectory(repositoryPackagePath);
		Directory.CreateDirectory(secondRepositoryPackagePath);
		Directory.CreateDirectory(environmentPackagePath);
		Directory.CreateDirectory(secondEnvironmentPackagePath);
		await File.WriteAllTextAsync(Path.Combine(repositoryPackagePath, "descriptor.json"), "{}");
		await File.WriteAllTextAsync(Path.Combine(repositoryPackagePath, "repo.txt"), "repository");
		await File.WriteAllTextAsync(Path.Combine(secondRepositoryPackagePath, "descriptor.json"), "{}");
		await File.WriteAllTextAsync(Path.Combine(secondRepositoryPackagePath, "repo.txt"), "repository");
		await File.WriteAllTextAsync(Path.Combine(environmentPackagePath, "env.txt"), "environment");
		await File.WriteAllTextAsync(Path.Combine(secondEnvironmentPackagePath, "env.txt"), "environment");

		CancellationTokenSource cancellationTokenSource = new(TimeSpan.FromMinutes(2));
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new LinkFromRepositoryArrangeContext(
			rootDirectory,
			environmentPackagesPath,
			repositoryRootPath,
			session,
			cancellationTokenSource);
	}

	[AllureStep("Act by invoking link-from-repository by explicit environment package path")]
	[AllureDescription("Act by discovering the direct-path MCP tool and invoking it with the arranged package root and repository root")]
	private static async Task<LinkFromRepositoryActResult> ActByEnvPackagePathAsync(
		LinkFromRepositoryArrangeContext arrangeContext,
		string packages) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(EnvPkgPathToolName,
			because: "the direct-path link-from-repository tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			EnvPkgPathToolName,
			new Dictionary<string, object?> {
				["envPkgPath"] = arrangeContext.EnvironmentPackagesPath,
				["repoPath"] = arrangeContext.RepositoryRootPath,
				["packages"] = packages
			},
			arrangeContext.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new LinkFromRepositoryActResult(callResult, execution);
	}

	[AllureStep("Act by invoking link-from-repository by environment name")]
	[AllureDescription("Act by discovering the environment-name MCP tool and invoking it with a requested environment key and the arranged repository root")]
	private static async Task<LinkFromRepositoryActResult> ActByEnvironmentAsync(
		LinkFromRepositoryArrangeContext arrangeContext,
		string environmentName,
		string packages) {
		IList<McpClientTool> tools = await arrangeContext.Session.ListToolsAsync(arrangeContext.CancellationTokenSource.Token);
		tools.Select(tool => tool.Name).Should().Contain(EnvironmentToolName,
			because: "the environment-name link-from-repository tool must be advertised before the end-to-end call can be executed");

		CallToolResult callResult = await arrangeContext.Session.CallToolAsync(
			EnvironmentToolName,
			new Dictionary<string, object?> {
				["environmentName"] = environmentName,
				["repoPath"] = arrangeContext.RepositoryRootPath,
				["packages"] = packages
			},
			arrangeContext.CancellationTokenSource.Token);

		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);
		return new LinkFromRepositoryActResult(callResult, execution);
	}

	[AllureStep("Assert symbolic links are supported in the current test environment")]
	[AllureDescription("Assert that the current machine can create directory symbolic links before running the direct-path success scenario")]
	private static void EnsureSymbolicLinksAreSupported(LinkFromRepositoryArrangeContext arrangeContext) {
		string probeLinkPath = Path.Combine(arrangeContext.RootDirectory, "probe-link");
		string probeTargetPath = Path.Combine(arrangeContext.RootDirectory, "probe-target");
		Directory.CreateDirectory(probeTargetPath);

		try {
			Directory.CreateSymbolicLink(probeLinkPath, probeTargetPath);
		}
		catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException) {
			Assert.Ignore($"Directory symbolic links are not available in this test environment: {ex.Message}");
		}
		finally {
			if (Directory.Exists(probeLinkPath)) {
				Directory.Delete(probeLinkPath);
			}

			if (Directory.Exists(probeTargetPath)) {
				Directory.Delete(probeTargetPath);
			}
		}
	}

	[AllureStep("Assert MCP tool result is successful")]
	[AllureDescription("Assert that the link-from-repository MCP call completed without an MCP error result")]
	private static void AssertToolCallSucceeded(LinkFromRepositoryActResult actResult) {
		actResult.CallResult.IsError.Should().NotBeTrue(
			because: "a successful link-from-repository invocation should return a normal MCP tool result");
	}

	[AllureStep("Assert link-from-repository command exit code")]
	[AllureDescription("Assert that the underlying link-from-repository command completed with exit code 0")]
	private static void AssertCommandExitCode(LinkFromRepositoryActResult actResult) {
		actResult.Execution.ExitCode.Should().Be(0,
			because: "the underlying link-from-repository command should complete successfully for a valid temporary repository and package root");
	}

	[AllureStep("Assert success output contains info message")]
	[AllureDescription("Assert that successful link-from-repository execution includes at least one Info log message in the MCP command output")]
	private static void AssertSuccessIncludesInfoMessage(LinkFromRepositoryActResult actResult) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "successful MCP command execution should emit human-readable log messages");
		actResult.Execution.Output!.Should().Contain(
			message => message.MessageType == LogDecoratorType.Info,
			because: "successful link-from-repository execution should report progress or completion using info-level log output");
	}

	[AllureStep("Assert target package folder became a symbolic link")]
	[AllureDescription("Assert that the target package folder now resolves as a symbolic link after the direct-path link-from-repository call")]
	private static void AssertPackageFolderWasReplacedWithSymbolicLink(
		LinkFromRepositoryArrangeContext arrangeContext,
		string packageName = "PkgA") {
		DirectoryInfo directoryInfo = new(Path.Combine(arrangeContext.EnvironmentPackagesPath, packageName));
		directoryInfo.Exists.Should().BeTrue(
			because: "the target package path should exist after the linking operation");
		directoryInfo.LinkTarget.Should().NotBeNull(
			because: "link-from-repository should replace the target package directory with a symbolic link");
	}

	[AllureStep("Assert symbolic link points to repository package")]
	[AllureDescription("Assert that the created symbolic link resolves to the repository package content folder")]
	private static void AssertSymbolicLinkTargetsRepositoryPackage(
		LinkFromRepositoryArrangeContext arrangeContext,
		string packageName) {
		DirectoryInfo environmentDirectory = new(Path.Combine(arrangeContext.EnvironmentPackagesPath, packageName));
		FileSystemInfo? resolvedTarget = environmentDirectory.ResolveLinkTarget(returnFinalTarget: false);
		resolvedTarget.Should().NotBeNull(
			because: "the created symbolic link should resolve to a concrete repository package directory");
		Path.GetFullPath(resolvedTarget!.FullName).Should().Be(Path.GetFullPath(Path.Combine(arrangeContext.RepositoryRootPath, "packages", packageName)),
			because: "the symbolic link should point to the repository package content that was requested through MCP");
	}

	[AllureStep("Assert failed link-from-repository request reported failure")]
	[AllureDescription("Assert that link-from-repository reports failure instead of succeeding silently when the request is invalid")]
	private static void AssertToolCallFailed(LinkFromRepositoryActResult actResult) {
		bool failed = actResult.CallResult.IsError == true || actResult.Execution.ExitCode != 0;
		failed.Should().BeTrue(
			because: "link-from-repository should fail when the requested input cannot be executed successfully");
	}

	[AllureStep("Assert failure output contains error message type")]
	[AllureDescription("Assert that failed link-from-repository execution emits at least one Error log message when execution output is available")]
	private static void AssertFailureIncludesErrorMessage(LinkFromRepositoryActResult actResult) {
		actResult.Execution.Output.Should().NotBeNullOrEmpty(
			because: "failed MCP command execution should emit human-readable diagnostics");
		actResult.Execution.Output!.Should().Contain(
			message => message.MessageType == LogDecoratorType.Error,
			because: "failed link-from-repository execution should report its diagnostics as error-level log output");
	}

	[AllureStep("Assert environment failure mentions platform restriction or missing environment")]
	[AllureDescription("Assert that the environment-name failure output explains either the Windows-only restriction or the missing environment name")]
	private static void AssertEnvironmentFailureMentionsPlatformOrMissingEnvironment(LinkFromRepositoryActResult actResult) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? [])
			.Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "failed environment-name execution should provide diagnostics that explain why linking did not run");
		combinedOutput.Should().MatchRegex(
			"(?is)(only supported on windows|not a registered environment|environment .* not found|error occurred invoking)",
			because: "the failure log should tell the user whether the environment-name flow was rejected because the environment was missing or because the platform does not support that mode");
	}

	[AllureStep("Assert missing package failure mentions requested package")]
	[AllureDescription("Assert that the direct-path failure output identifies the missing repository package name")]
	private static void AssertMissingPackageFailureMentionsPackageName(LinkFromRepositoryActResult actResult, string packageName) {
		string combinedOutput = string.Join(
			Environment.NewLine,
			(actResult.Execution.Output ?? [])
			.Select(message => $"{message.MessageType}: {message.Value}"));

		combinedOutput.Should().NotBeNullOrWhiteSpace(
			because: "failed direct-path execution should explain why the requested package could not be linked");
		combinedOutput.Should().MatchRegex(
			$"(?is)({Regex.Escape(packageName)}|not found in repository|error occurred invoking)",
			because: "the failure log should help a human understand that the requested package did not exist in the repository");
	}

	[AllureStep("Assert missing package folder was not created")]
	[AllureDescription("Assert that the failed direct-path request does not create a package directory for a missing repository package")]
	private static void AssertPackageFolderWasNotCreated(LinkFromRepositoryArrangeContext arrangeContext, string packageName) {
		string missingPackagePath = Path.Combine(arrangeContext.EnvironmentPackagesPath, packageName);
		Directory.Exists(missingPackagePath).Should().BeFalse(
			because: "a failed link-from-repository request should not create a new package directory for a package that does not exist in the repository");
	}

	private sealed record LinkFromRepositoryArrangeContext(
		string RootDirectory,
		string EnvironmentPackagesPath,
		string RepositoryRootPath,
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

	private sealed record LinkFromRepositoryActResult(
		CallToolResult CallResult,
		CommandExecutionEnvelope Execution);
}
