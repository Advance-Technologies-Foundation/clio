using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("theme-background")]
[NonParallelizable]
public sealed class ThemeBackgroundToolE2ETests {

	private const string ThemeId = "4ecdda0a-3f24-4d5c-9647-46b6d4fe0a51";

	private static RuntimeDetectionStubServerConfiguration Stub(string coreVersion) =>
		new(NetCoreHealthEnabled: true, NetFrameworkHealthEnabled: false, NetCoreServiceEnabled: true,
			NetFrameworkServiceEnabled: false, NetCoreUiMarkerEnabled: true, CoreVersion: coreVersion,
			ThemeWriteResponseJson: "{\"success\":true}");

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[Description("Creates a theme with a background color through the real MCP server against a 10.2.96 stub.")]
	public Task CreateTheme_Should_Write_The_Background() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", Stub("10.2.96.0"),
			async (session, environmentName, _, cancellationToken) => {
				CreateThemeResult result = EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(
					await session.CallToolAsync(CreateThemeTool.ToolName, new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["caption"] = "Background probe",
							["css-content"] = ".background-probe{color:#003366}",
							["background-color"] = "#1A2B3C",
							["keep-icon-background"] = true
						}
					}, cancellationToken));

				result.Success.Should().BeTrue(
					because: $"a theme and its background are written by one call (error: {result.Error})");
			});

	[Test]
	[AllureTag(UpdateThemeTool.ToolName)]
	[Description("Changes only the background of a theme through the real MCP server against a 10.2.96 stub, without caption, css-class-name or css-content.")]
	public Task UpdateTheme_Should_Write_Only_The_Background() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", Stub("10.2.96.0"),
			async (session, environmentName, _, cancellationToken) => {
				CommandExecutionEnvelope result = McpCommandExecutionParser.Extract(
					await session.CallToolAsync(UpdateThemeTool.ToolName, new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["id"] = ThemeId,
							["background-color"] = "#1A2B3C",
							["keep-icon-background"] = true
						}
					}, cancellationToken));

				result.ExitCode.Should().Be(0, because: "a background-only update is a valid call");
			});

	[TestCase(CreateThemeTool.ToolName, "CreateTheme")]
	[TestCase(UpdateThemeTool.ToolName, "UpdateTheme")]
	[AllureTag(CreateThemeTool.ToolName, UpdateThemeTool.ToolName)]
	[Description("Refuses a theme background below 10.2.96 with version-too-old, before any theme write.")]
	public Task Theme_Write_Should_Refuse_A_Background_Below_The_Floor(string toolName, string operation) =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", Stub("10.2.95.0"),
			async (session, environmentName, stubServer, cancellationToken) => {
				CallToolResult callResult = await session.CallToolAsync(toolName, new Dictionary<string, object?> {
					["args"] = new Dictionary<string, object?> {
						["environment-name"] = environmentName,
						["id"] = ThemeId,
						["caption"] = "Floor probe",
						["css-content"] = ".floor-probe{color:#003366}",
						["background-color"] = "#1A2B3C"
					}
				}, cancellationToken);
				string refusal = toolName == CreateThemeTool.ToolName
					? EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(callResult).Error
					: string.Join(" ", McpCommandExecutionParser.Extract(callResult).Output.Select(message => message.Value));
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync();

				refusal.Should().Contain(CreatioVersionRequirementException.VersionTooOldCode,
					because: "an older ThemeService would drop the background silently, so clio refuses it");
				requests.Should().NotContain(request => request.Url.EndsWith("/ThemeService.svc/" + operation),
					because: "the floor is checked before any write");
			});
}
