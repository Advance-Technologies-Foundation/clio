using System.Text.Json;
using System.Text.RegularExpressions;
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
/// End-to-end proof on a disposable sandbox that place-section-in-workplace moves a new app out of
/// <c>My applications</c> into a new workplace with a complete binding and a role grant, that a second identical
/// call changes nothing, and that mode=none only reports.
/// </summary>
[TestFixture]
[Category("McpE2E.Sandbox")]
[AllureNUnit]
[AllureFeature("place-section-in-workplace")]
[NonParallelizable]
public sealed class PlaceSectionInWorkplaceToolSandboxE2ETests : McpContractFixtureBase {

	private const string ToolName = PlaceSectionInWorkplaceTool.ToolName;
	private const string GrantedRole = "System administrators";
	private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";
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
	[AllureName("place-section-in-workplace moves a new app into a new workplace, binds it, and converges")]
	[Description("Creates an app (create-app places it in My applications), moves its section into a new uniquely named workplace with a role grant, verifies the live rows (the section is gone from My applications, Type and LoaderId are filled) and the shipped workplace binding columns, then verifies that a re-run changes nothing and that mode=none reports the same location.")]
	public async Task PlaceSectionInWorkplace_Should_Move_New_App_Bind_Grant_And_Converge() {
		// Arrange
		await using ArrangeContext context = Arrange(TimeSpan.FromMinutes(10));
		string suffix = Guid.NewGuid().ToString("N")[..8];
		ApplicationContextResponseEnvelope app = await CreateAppAsync(context, suffix);
		string workplaceName = "ClioE2E Workplace " + suffix;

		// Act
		PlaceSectionInWorkplaceResponse first = await PlaceAsync(context, app, workplaceName, "new");
		PlaceSectionInWorkplaceResponse second = await PlaceAsync(context, app, workplaceName, "new");
		PlaceSectionInWorkplaceResponse report = await PlaceAsync(context, app, null, "none");
		string bindingText = await ReadBindingAsync(context, app.PackageName!, first.Workplace?.BindingName);
		JsonElement liveWorkplace = await ODataAsync(context,
			$"odata/SysWorkplace({first.Workplace?.Id})?$select=TypeId,LoaderId,SysApplicationClientTypeId");
		JsonElement livePlacements = await ODataAsync(context,
			$"odata/SysModuleInWorkplace?$select=Id,SysWorkplaceId&$filter=SysModule/Id%20eq%20{first.Section?.SectionId}");

		// Assert
		AllureApi.Step("Assert the first call created the workplace, moved the section and granted the role", () => {
			first.Success.Should().BeTrue(because: first.Error ?? "the placement succeeds");
			first.Workplace!.Created.Should().BeTrue(because: "the workplace name is unique to this run");
			first.Section!.Action.Should().Be("moved", because: "create-app put the section in My applications");
			first.Roles!.Single().Action.Should().Be("granted", because: "the new workplace had no grant");
			first.SectionWorkplaces!.Select(location => location.Id).Should().Equal([first.Workplace.Id],
				because: "after a move the section is only in the target");
			first.NextStep.Should().Contain("log out", because: "navigation lists are cached per session");
		});
		AllureApi.Step("Assert the live rows", () => {
			liveWorkplace.GetProperty("TypeId").GetString().Should().NotBe(EmptyGuid,
				because: "the platform fills Type on the DataService insert");
			liveWorkplace.GetProperty("LoaderId").GetString().Should().NotBe(EmptyGuid,
				because: "the platform fills LoaderId on the DataService insert");
			livePlacements.GetProperty("value").EnumerateArray()
				.Select(row => row.GetProperty("SysWorkplaceId").GetString())
				.Should().Equal([first.Workplace!.Id.ToString()],
					because: "the one placement row now belongs to the target workplace");
		});
		AllureApi.Step("Assert the workplace binding ships the full column set", () => {
			// Compare exact column names: a substring check passes "Type" through "SysApplicationClientType".
			Match columnsLine = Regex.Match(bindingText, @"columns \(\d+\): ([^""\r\n]*)");
			columnsLine.Success.Should().BeTrue(because: "read-data-binding-db reports the bound columns");
			string[] columns = columnsLine.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
			columns.Should().Contain(["LoaderId", "Type", "SysApplicationClientType", "Position", "Name"],
				because: "an install supplies no default for any of these columns");
		});
		AllureApi.Step("Assert the second call changed nothing", () => {
			second.Success.Should().BeTrue(because: second.Error ?? "a re-run with mode=new finds its own workplace");
			second.Workplace!.Id.Should().Be(first.Workplace!.Id, because: "the same workplace is reused");
			second.Workplace.Created.Should().BeFalse(because: "nothing new is created");
			second.Section!.Action.Should().Be("already-present", because: "the placement exists");
			second.Section.PlacementId.Should().Be(first.Section!.PlacementId, because: "no duplicate placement");
			second.Roles!.Single().Action.Should().Be("already-granted", because: "the grant exists");
		});
		AllureApi.Step("Assert mode=none only reports", () => {
			report.Success.Should().BeTrue(because: report.Error ?? "mode=none needs no workplace name");
			report.Workplace.Should().BeNull(because: "mode=none targets no workplace");
			report.SectionWorkplaces!.Select(location => location.Id).Should().Equal([first.Workplace!.Id],
				because: "the report names where the section is");
		});
	}

	private async Task<ApplicationContextResponseEnvelope> CreateAppAsync(ArrangeContext context, string suffix) {
		CallToolResult callResult = await context.Session.CallDestructiveAsync(
			ApplicationCreateTool.ApplicationCreateToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["name"] = $"Workplace e2e {suffix}",
				["code"] = $"WpE2E{suffix}",
				["template-code"] = "AppFreedomUI",
				["with-mobile-pages"] = false
			},
			context.CancellationTokenSource.Token);
		ApplicationContextResponseEnvelope app = ApplicationResultParser.ExtractInfo(callResult);
		app.Success.Should().BeTrue(because: app.Error ?? "the test needs its own app in My applications");
		return app;
	}

	private async Task<PlaceSectionInWorkplaceResponse> PlaceAsync(ArrangeContext context,
		ApplicationContextResponseEnvelope app, string? workplaceName, string mode) {
		CallToolResult callResult = await context.Session.CallDestructiveAsync(ToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["package-name"] = app.PackageName,
				["section-code"] = app.ApplicationCode,
				["workplace-name"] = workplaceName,
				["roles"] = mode == "none" ? null : new[] { GrantedRole },
				["mode"] = mode
			},
			context.CancellationTokenSource.Token);
		callResult.IsError.Should().NotBeTrue(because: "the tool returns a structured result");
		return EntitySchemaStructuredResultParser.Extract<PlaceSectionInWorkplaceResponse>(callResult);
	}

	private async Task<string> ReadBindingAsync(ArrangeContext context, string packageName, string? bindingName) {
		CallToolResult callResult = await context.Session.CallDestructiveAsync(
			ReadDataBindingDbTool.ReadDataBindingDbToolName,
			new Dictionary<string, object?> {
				["environment-name"] = _environmentName,
				["package-name"] = packageName,
				["binding-name"] = bindingName
			},
			context.CancellationTokenSource.Token);
		return string.Join("\n", callResult.Content.OfType<TextContentBlock>().Select(block => block.Text));
	}

	private async Task<JsonElement> ODataAsync(ArrangeContext context, string servicePath) {
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(_settings,
			["call-service", "--method", "GET", "-e", _environmentName, "--service-path", servicePath],
			cancellationToken: context.CancellationTokenSource.Token);
		result.ExitCode.Should().Be(0, because: result.StandardError);
		string output = result.StandardOutput;
		return JsonDocument.Parse(output[output.IndexOf('{')..]).RootElement.Clone();
	}
}
