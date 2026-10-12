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
/// End-to-end proof on a disposable sandbox that place-section-in-workplace creates a workplace with a complete
/// binding, places the seeded application's section, grants a role, and that a second identical call changes
/// nothing.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("place-section-in-workplace")]
[NonParallelizable]
public sealed class PlaceSectionInWorkplaceToolSandboxE2ETests : McpContractFixtureBase {

	private const string ToolName = PlaceSectionInWorkplaceTool.ToolName;
	// The application the sandbox is seeded with; ApplicationToolE2ETests relies on the same one.
	private const string SeededApplicationCode = "AutoTestClioMcp";
	private const string GrantedRole = "System administrators";
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
	[AllureName("place-section-in-workplace creates, places, grants, binds, and converges on a re-run")]
	[Description("Creates a package, places the seeded application's section in a new uniquely named workplace with a role grant (keeping its My applications placement), verifies the bound workplace columns and the live Type/LoaderId, then verifies a second call reports everything as already present.")]
	public async Task PlaceSectionInWorkplace_Should_Create_Place_Grant_And_Converge() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(5));
		await SeededApplicationResolver.ResolveOrIgnoreAsync(context.Session, context.CancellationTokenSource.Token,
			_environmentName, SeededApplicationCode);
		string packageName = await CreatePackageAsync(context, "ClioE2EWp" + Guid.NewGuid().ToString("N")[..10]);
		string workplaceName = "ClioE2E Workplace " + Guid.NewGuid().ToString("N")[..8];
		try {
			// Act
			PlaceSectionInWorkplaceResponse first = await PlaceAsync(context, packageName, workplaceName);
			PlaceSectionInWorkplaceResponse second = await PlaceAsync(context, packageName, workplaceName);
			CallToolResult binding = await context.Session.CallToolAsync(ReadDataBindingDbTool.ReadDataBindingDbToolName,
				new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = _environmentName,
						["package-name"] = packageName,
						["binding-name"] = first.Workplace?.BindingName
					}
				},
				context.CancellationTokenSource.Token);
			string bindingText = string.Join("\n", binding.Content.OfType<TextContentBlock>().Select(block => block.Text));
			ClioCliCommandResult live = await ClioCliCommandRunner.RunAsync(_settings, [
				"call-service", "--method", "GET", "-e", _environmentName, "--service-path",
				$"odata/SysWorkplace({first.Workplace?.Id})?$select=TypeId,LoaderId,SysApplicationClientTypeId"
			], cancellationToken: context.CancellationTokenSource.Token);

			// Assert
			AllureApi.Step("Assert the first call created, placed and granted", () => {
				first.Success.Should().BeTrue(because: first.Error ?? "the placement succeeds");
				first.Workplace!.Created.Should().BeTrue(because: "the workplace name is unique to this run");
				first.Section!.Action.Should().Be("created", because: "remove-from-my-applications=false adds a placement");
				first.Roles!.Single().Action.Should().Be("granted", because: "the new workplace had no grant");
				first.NextStep.Should().Contain("log out", because: "navigation lists are cached per session");
				first.SectionWorkplaces!.Select(location => location.Id).Should().Contain(first.Workplace.Id,
					because: "the response says where the section ended up");
			});
			AllureApi.Step("Assert the workplace binding and live row are complete", () => {
				bindingText.Should().Contain("LoaderId").And.Contain("Type").And.Contain("SysApplicationClientType",
					because: "an install supplies no defaults, so the binding must carry the full column set");
				live.ExitCode.Should().Be(0, because: live.StandardError);
				live.StandardOutput.Should().NotContain("\"LoaderId\": \"00000000-0000-0000-0000-000000000000\"",
					because: "the platform fills LoaderId on the live insert");
			});
			AllureApi.Step("Assert the second call changed nothing", () => {
				second.Success.Should().BeTrue(because: second.Error ?? "a re-run succeeds");
				second.Workplace!.Id.Should().Be(first.Workplace!.Id, because: "the same workplace is reused");
				second.Workplace.Created.Should().BeFalse(because: "nothing new is created");
				second.Section!.Action.Should().Be("already-present", because: "the placement exists");
				second.Section.PlacementId.Should().Be(first.Section!.PlacementId, because: "no duplicate placement");
				second.Roles!.Single().Action.Should().Be("already-granted", because: "the grant exists");
			});
		}
		finally {
			await DeletePackageAsync(packageName);
		}
	}

	private async Task<PlaceSectionInWorkplaceResponse> PlaceAsync(ArrangeContext context, string packageName,
		string workplaceName) {
		CallToolResult callResult = await context.Session.CallDestructiveAsync(ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["package-name"] = packageName,
				["section-code"] = SeededApplicationCode,
				["workplace-name"] = workplaceName,
				["roles"] = new[] { GrantedRole },
				["remove-from-my-applications"] = false
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "the tool returns a structured result");
		return EntitySchemaStructuredResultParser.Extract<PlaceSectionInWorkplaceResponse>(callResult);
	}

	private async Task<string> CreatePackageAsync(ArrangeContext context, string packageName) {
		CallToolResult callResult = await context.Session.CallDestructiveAsync(CreatePackageTool.CreatePackageToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["package-name"] = packageName,
				["dependencies"] = new[] { "CrtBase" }
			},
			context.CancellationTokenSource.Token);
		CreatePackageResponse created = EntitySchemaStructuredResultParser.Extract<CreatePackageResponse>(callResult);
		created.Success.Should().BeTrue(because: created.Error ?? "the test needs its own package to bind into");
		return created.PackageName!;
	}

	private async Task DeletePackageAsync(string packageName) {
		string? cleanupError = await BoundedCleanup.RunAsync(
			async token => (await ClioCliCommandRunner.RunAsync(_settings,
				["delete-pkg-remote", packageName, "-e", _environmentName], cancellationToken: token)).ExitCode,
			TimeSpan.FromMinutes(2), $"Deleting package {packageName}");
		if (cleanupError is not null) {
			TestContext.Progress.WriteLine(cleanupError);
		}
	}
}
