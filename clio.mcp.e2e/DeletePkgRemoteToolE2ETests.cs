using Allure.Net.Commons;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// End-to-end tests for the delete-pkg-remote MCP tool that need no Creatio environment: the tool is
/// reachable on the lazy surface, its args wrapper binds to a structured validation failure, and an
/// environment it cannot resolve is refused as unavailable rather than deleted blind.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("delete-pkg-remote")]
[Parallelizable(ParallelScope.Self)]
public sealed class DeletePkgRemoteToolE2ETests : McpContractFixtureBase {

	private const string ToolName = DeletePkgRemoteTool.DeletePkgRemoteToolName;

	[Test]
	[AllureTag(ToolName)]
	[AllureName("delete-pkg-remote is discoverable on the lazy surface")]
	[AllureDescription("Starts the real clio MCP server and verifies delete-pkg-remote is discoverable via the get-tool-contract compact index.")]
	[Description("Starts the real clio MCP server and verifies delete-pkg-remote is discoverable via the get-tool-contract compact index.")]
	public async Task DeletePkgRemote_Should_Be_Discoverable_On_Lazy_Surface() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		IReadOnlyCollection<string> toolNames =
			await context.Session.ListReachableToolNamesAsync(context.CancellationTokenSource.Token);

		// Assert
		toolNames.Should().Contain(ToolName,
			because: "delete-pkg-remote must be reachable on the lazy surface even though it is not resident in tools/list");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("delete-pkg-remote binds the args wrapper and returns a structured validation failure")]
	[AllureDescription("Calls delete-pkg-remote through the real MCP server without an environment-name and verifies the structured failure names the field.")]
	[Description("Calls delete-pkg-remote through the real MCP server without an environment-name and verifies the structured failure names the field.")]
	public async Task DeletePkgRemote_Should_Return_Structured_Validation_Failure_When_Environment_Is_Missing() {
		// Arrange
		await using ArrangeContext context = Arrange();

		// Act
		CallToolResult callResult = await context.Session.CallDestructiveAsync(ToolName,
			new Dictionary<string, object?> { ["package-name"] = "UsrSomeScratchPkg" },
			context.CancellationTokenSource.Token);
		DeletePkgRemoteResponse result = EntitySchemaStructuredResultParser.Extract<DeletePkgRemoteResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "an argument mistake must surface as a structured in-tool failure, not an MCP protocol error");
		result.Success.Should().BeFalse(because: "no environment was named, so nothing may be deleted");
		result.Error.Should().Contain("environment-name",
			because: "the failure names the field the caller has to add");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("delete-pkg-remote refuses an environment it cannot resolve as unavailable")]
	[AllureDescription("Calls delete-pkg-remote through the real MCP server with an unregistered environment name and verifies the structured refusal deletes nothing.")]
	[Description("Calls delete-pkg-remote through the real MCP server with an unregistered environment name and verifies the structured refusal deletes nothing.")]
	public async Task DeletePkgRemote_Should_Refuse_An_Unresolvable_Environment_As_Unavailable() {
		// Arrange
		await using ArrangeContext context = Arrange();
		string unknownEnvironment = $"missing-delete-env-{Guid.NewGuid():N}";

		// Act
		CallToolResult callResult = await context.Session.CallDestructiveAsync(ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = unknownEnvironment,
				["package-name"] = "UsrSomeScratchPkg"
			},
			context.CancellationTokenSource.Token);
		DeletePkgRemoteResponse result = EntitySchemaStructuredResultParser.Extract<DeletePkgRemoteResponse>(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(because: "a resolution refusal is a structured in-tool failure");
		result.Success.Should().BeFalse(because: "the environment could not be read, so nothing may be deleted");
		result.Status.Should().Be(DeletePkgRemoteTool.StatusUnavailable,
			because: "an environment that cannot be read is refused as unavailable rather than deleted blind");
		result.Error.Should().NotBeNullOrWhiteSpace(because: "the refusal must say the environment could not be read");
	}
}

/// <summary>
/// End-to-end proof on a disposable sandbox that delete-pkg-remote deletes a scratch package created by
/// create-package and verifies the absence, refuses a name that is not installed, and refuses a package
/// other packages depend on until the dependency is gone.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("delete-pkg-remote")]
[NonParallelizable]
public sealed class DeletePkgRemoteToolSandboxE2ETests : McpContractFixtureBase {

	private const string ToolName = DeletePkgRemoteTool.DeletePkgRemoteToolName;
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
	[AllureTag(ToolName)]
	[AllureName("delete-pkg-remote deletes a created package and verifies the absence")]
	[AllureDescription("Creates a uniquely named package through create-package, deletes it through delete-pkg-remote, and verifies the read-back and list-packages both report it gone.")]
	[Description("Creates a uniquely named package through create-package, deletes it through delete-pkg-remote, and verifies the read-back and list-packages both report it gone.")]
	public async Task DeletePkgRemote_Should_Delete_Created_Package_And_Verify_Absence() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		string requestedName = "ClioE2EDel" + Guid.NewGuid().ToString("N")[..8];
		string? createdName = null;
		try {
			// Act
			CreatePackageResponse created = await CreateAsync(context, requestedName);
			createdName = created.PackageName;
			DeletePkgRemoteResponse deleted = await DeleteAsync(context, createdName);
			IReadOnlyList<GetPkgListEnvelope> listedAfter = await ListAsync(context, createdName);

			// Assert
			AllureApi.Step("Assert the package was created", () => {
				created.Success.Should().BeTrue(because: created.Error ?? "the scratch package is created for the delete");
				created.PackageCreated.Should().BeTrue(because: "the package now exists");
			});
			AllureApi.Step("Assert the delete succeeded and the read-back confirmed the absence", () => {
				deleted.Success.Should().BeTrue(because: deleted.Error ?? "the scratch package is editable and has no dependents");
				deleted.Status.Should().Be(DeletePkgRemoteTool.StatusDeleted,
					because: "both the delete call and the read-back must agree the package is gone");
				deleted.Deleted.Should().BeTrue(because: "the read-back found no SysPackage row");
				deleted.PackageUId.Should().Be(created.PackageUId,
					because: "the report carries the identifier it deleted");
			});
			AllureApi.Step("Assert the package is gone from list-packages", () => {
				listedAfter.Select(package => package.Name).Should().NotContain(createdName,
					because: "an independent list-packages read must agree the package is gone");
			});
		}
		finally {
			await CleanupAsync(createdName);
		}
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("delete-pkg-remote reports not-found for a name that is not installed")]
	[AllureDescription("Calls delete-pkg-remote with a name no package has and verifies the structured not-found refusal.")]
	[Description("Calls delete-pkg-remote with a name no package has and verifies the structured not-found refusal.")]
	public async Task DeletePkgRemote_Should_Report_NotFound_When_No_Package_Has_The_Name() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		string absentName = "ClioE2ENo" + Guid.NewGuid().ToString("N")[..8];

		// Act
		DeletePkgRemoteResponse result = await DeleteAsync(context, absentName);

		// Assert
		result.Success.Should().BeFalse(because: "there was nothing to delete");
		result.Status.Should().Be(DeletePkgRemoteTool.StatusNotFound,
			because: "the pre-flight found no SysPackage row for the name");
		result.Deleted.Should().BeFalse(because: "the call deleted nothing");
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("delete-pkg-remote refuses a package its dependents ship against until the dependency is gone")]
	[AllureDescription("Creates two packages where the second depends on the first, verifies the first delete is refused naming the dependent, then deletes the dependent and the first.")]
	[Description("Creates two packages where the second depends on the first, verifies the first delete is refused naming the dependent, then deletes the dependent and the first.")]
	public async Task DeletePkgRemote_Should_Refuse_A_Depended_On_Package_Until_The_Dependency_Is_Gone() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		string baseName = "ClioE2EDep" + Guid.NewGuid().ToString("N")[..8];
		string dependentName = baseName + "Child";
		string? createdBase = null;
		string? createdDependent = null;
		try {
			// Act
			CreatePackageResponse baseResponse = await CreateAsync(context, baseName);
			createdBase = baseResponse.PackageName;
			CreatePackageResponse dependentResponse = await CreateAsync(context, dependentName, createdBase);
			createdDependent = dependentResponse.PackageName;
			DeletePkgRemoteResponse blocked = await DeleteAsync(context, createdBase);
			DeletePkgRemoteResponse dependentDeleted = await DeleteAsync(context, createdDependent);
			DeletePkgRemoteResponse baseDeleted = await DeleteAsync(context, createdBase);

			// Assert
			AllureApi.Step("Assert the depended-on package is refused, naming the dependent", () => {
				blocked.Success.Should().BeFalse(because: "deleting it would leave its dependent without a dependency");
				blocked.Status.Should().Be(DeletePkgRemoteTool.StatusBlockedByDependents,
					because: "SysPackageDependency has a row pointing at the package");
				blocked.Dependents.Should().Contain(createdDependent,
					because: "the caller needs the exact dependent name to unblock the delete");
			});
			AllureApi.Step("Assert the dependent deletes, unblocking the base package", () => {
				dependentDeleted.Success.Should().BeTrue(because: dependentDeleted.Error ?? "the dependent has no dependents of its own");
				dependentDeleted.Status.Should().Be(DeletePkgRemoteTool.StatusDeleted,
					because: "the dependent is gone, so the base package has no dependents left");
			});
			AllureApi.Step("Assert the base package deletes once unblocked", () => {
				baseDeleted.Success.Should().BeTrue(because: baseDeleted.Error ?? "no dependent remains");
				baseDeleted.Status.Should().Be(DeletePkgRemoteTool.StatusDeleted,
					because: "with its dependent gone, the base package is deletable");
			});
		}
		finally {
			await CleanupAsync(createdBase, createdDependent);
		}
	}

	private async Task CleanupAsync(params string?[] packageNames) {
		foreach (string? packageName in packageNames) {
			if (packageName is null) {
				continue;
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

	private async Task<CreatePackageResponse> CreateAsync(ArrangeContext context, string packageName,
		string? dependency = null) {
		Dictionary<string, object?> arguments = new() {
			["environment-name"] = _environmentName,
			["package-name"] = packageName,
			["description"] = "clio MCP e2e"
		};
		if (dependency is not null) {
			arguments["dependencies"] = new[] { dependency };
		}
		CallToolResult callResult = await context.Session.CallDestructiveAsync(CreatePackageTool.CreatePackageToolName,
			arguments, context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "create-package returns a structured result");
		return EntitySchemaStructuredResultParser.Extract<CreatePackageResponse>(callResult);
	}

	private async Task<DeletePkgRemoteResponse> DeleteAsync(ArrangeContext context, string packageName) {
		CallToolResult callResult = await context.Session.CallDestructiveAsync(ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["package-name"] = packageName
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "delete-pkg-remote returns a structured result");
		return EntitySchemaStructuredResultParser.Extract<DeletePkgRemoteResponse>(callResult);
	}

	private async Task<IReadOnlyList<GetPkgListEnvelope>> ListAsync(ArrangeContext context, string packageName) {
		CallToolResult callResult = await context.Session.CallToolAsync("list-packages",
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = _environmentName,
					["filter"] = packageName
				}
			},
			context.CancellationTokenSource.Token);
		return GetPkgListResultParser.Extract(callResult);
	}
}
