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
/// End-to-end proof on a disposable sandbox that create-package creates an editable package with its
/// dependencies, that the package is listed and accepted as a write target, and that a repeated name is
/// refused without changes.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("create-package")]
[NonParallelizable]
public sealed class CreatePackageToolSandboxE2ETests : McpContractFixtureBase {

	private const string ToolName = CreatePackageTool.CreatePackageToolName;
	// The application the sandbox is seeded with; ApplicationToolE2ETests relies on the same one.
	private const string SeededApplicationCode = "AutoTestClioMcp";
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
	[AllureName("create-package creates an editable package and refuses a duplicate")]
	[Description("Creates a uniquely named package with a dependency, verifies the readback, list-packages and get-target-package, then verifies a second call with the same name is refused and changes nothing.")]
	public async Task CreatePackage_Should_Create_Editable_Package_And_Refuse_Duplicate() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		string requestedName = "ClioE2E" + Guid.NewGuid().ToString("N")[..10];
		string? createdName = null;
		try {
			// Act
			CreatePackageResponse created = await CreateAsync(context, requestedName);
			createdName = created.PackageName;
			CreatePackageResponse duplicate = await CreateAsync(context, requestedName);
			IReadOnlyList<GetPkgListEnvelope> listed = await ListAsync(context, created.PackageName!);
			GetTargetPackageResponse target = await GetTargetAsync(context, created.PackageName!);

			// Assert
			AllureApi.Step("Assert the package was created with its readback", () => {
				created.Success.Should().BeTrue(because: created.Error ?? "the package is created with its dependency");
				created.PackageCreated.Should().BeTrue(because: "the package now exists");
				created.PackageName.Should().EndWith(requestedName,
					because: "the stored name is the requested one, prefixed when the environment has a prefix");
				created.Editable.Should().BeTrue(because: "a created package is editable, unlike one pushed from a workspace");
				created.InstallType.Should().Be(0, because: "InstallType 0 is the editable state");
				created.Dependencies.Should().Contain("CrtBase", because: "the requested dependency is applied after creation");
				created.Maintainer.Should().NotBeNullOrWhiteSpace(because: "the environment assigns the maintainer");
			});
			AllureApi.Step("Assert the package is listed and accepted as a write target", () => {
				listed.Select(package => package.Name).Should().Contain(created.PackageName,
					because: "a created package appears in list-packages");
				target.Success.Should().BeTrue(because: target.Error ?? "get-target-package accepts the created package");
			});
			AllureApi.Step("Assert a repeated name is refused without changes", () => {
				duplicate.Success.Should().BeFalse(because: "an existing package must never be overwritten");
				duplicate.PackageCreated.Should().BeFalse(because: "the refusal changes nothing");
				duplicate.Error.Should().Contain("already exists", because: "the reason is relayed to the agent");
			});
		}
		finally {
			await DeleteAsync(createdName);
		}
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("create-package creates a package inside an installed application")]
	[Description("Creates a package with application-code set to the seeded application and verifies, through an independent OData read of SysPackageInInstalledApp, that the created package UId belongs to that application.")]
	public async Task CreatePackage_Should_Create_Package_Inside_Installed_Application() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		await SeededApplicationResolver.ResolveOrIgnoreAsync(context.Session, context.CancellationTokenSource.Token,
			_environmentName, SeededApplicationCode);
		string requestedName = "ClioE2EApp" + Guid.NewGuid().ToString("N")[..10];
		string? createdName = null;
		try {
			// Act
			CreatePackageResponse created = await CreateAsync(context, requestedName, SeededApplicationCode);
			createdName = created.PackageName;
			ClioCliCommandResult membership = await ClioCliCommandRunner.RunAsync(_settings, [
				"call-service", "--method", "GET", "-e", _environmentName, "--service-path",
				$"odata/SysPackageInInstalledApp?$filter=SysPackage/UId%20eq%20{created.PackageUId}"
					+ "&$expand=SysInstalledApp($select=Code)"
			], cancellationToken: context.CancellationTokenSource.Token);

			// Assert
			AllureApi.Step("Assert the package was created in the application", () => {
				created.Success.Should().BeTrue(because: created.Error ?? "the package is created inside the application");
				created.ApplicationCode.Should().Be(SeededApplicationCode, because: "the readback names the application");
				membership.ExitCode.Should().Be(0, because: membership.StandardError);
				membership.StandardOutput.Should().Contain($"\"Code\": \"{SeededApplicationCode}\"",
					because: "the platform must record the created package as part of the application");
			});
		}
		finally {
			await DeleteAsync(createdName);
		}
	}

	private async Task DeleteAsync(string? packageName) {
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

	private async Task<CreatePackageResponse> CreateAsync(ArrangeContext context, string packageName,
		string? applicationCode = null) {
		CallToolResult callResult = await context.Session.CallDestructiveAsync(ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["package-name"] = packageName,
				["description"] = "clio MCP e2e",
				["dependencies"] = new[] { "CrtBase" },
				["application-code"] = applicationCode
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "create-package returns a structured result");
		return EntitySchemaStructuredResultParser.Extract<CreatePackageResponse>(callResult);
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

	private async Task<GetTargetPackageResponse> GetTargetAsync(ArrangeContext context, string packageName) {
		CallToolResult callResult = await context.Session.CallToolAsync(GetTargetPackageTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = _environmentName,
					["package"] = packageName
				}
			},
			context.CancellationTokenSource.Token);
		return EntitySchemaStructuredResultParser.Extract<GetTargetPackageResponse>(callResult);
	}
}
