using System.Collections.Generic;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;

namespace Clio.Mcp.E2E;

[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature("theme-background")]
[NonParallelizable]
public sealed class ThemeBackgroundToolE2ETests {

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
}
