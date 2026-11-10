using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Command.Theming;
using Clio.Common;
using Clio.Mcp.E2E.Support.Creatio;
using Clio.Mcp.E2E.Support.Mcp;
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

	private const string ImageId = "6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81";

	private const string ColorBackground = "\"background\":{\"mode\":\"Color\",\"color\":\"#1A2B3C\"}";

	private const string ImageBackground = "\"background\":{\"mode\":\"Image\",\"imageId\":\"" + ImageId + "\"}";

	private const string CatalogJson = "{\"success\":true,\"values\":[{\"id\":\"" + ThemeId + "\",\"caption\":\"Gallery\"," +
		"\"cssClassName\":\"gallery\",\"cssFilePath\":\"Terrasoft.Configuration/Pkg/UsrBranding/Files/themes/" + ThemeId +
		"/theme.css\"}]}";

	private static readonly string[] ContentKeys = ["\"caption\"", "\"cssClassName\"", "\"cssContent\""];

	private static RuntimeDetectionStubServerConfiguration Stub(string coreVersion) =>
		new(NetCoreHealthEnabled: true, NetFrameworkHealthEnabled: false, NetCoreServiceEnabled: true,
			NetFrameworkServiceEnabled: false, NetCoreUiMarkerEnabled: true, CoreVersion: coreVersion,
			ThemeWriteResponseJson: "{\"success\":true}");

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme writes a background color in the same CreateTheme call")]
	[AllureDescription("Calls create-theme with background-color and keep-icon-background through the real MCP server against a 10.2.96 stub, then checks the result reports the panel icon background as kept with no warnings and the CreateTheme body carries the background.")]
	[Description("Creates a theme with a background color through the real MCP server against a 10.2.96 stub.")]
	public Task CreateTheme_Should_Write_The_Background() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", Stub("10.2.96.0"),
			async (session, environmentName, stubServer, cancellationToken) => {
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
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);

				result.Success.Should().BeTrue(
					because: $"a theme and its background are written by one call (error: {result.Error})");
				result.PanelIconBackground.Should().Be(ThemeBackgroundOutcome.PanelIconKept,
					because: "keep-icon-background leaves the UsePanelIconBackground feature as it is");
				result.Warnings.Should().BeNull(because: "nothing failed after the theme write");
				ThemeWriteBody(requests, "CreateTheme").Should().Contain(ColorBackground,
					because: "the background must reach ThemeService in the CreateTheme call itself");
			});

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme registers the background image in the background gallery before the theme write")]
	[AllureDescription("Calls create-theme with background-image-id and keep-icon-background through the real MCP server against a 10.2.96 stub whose background gallery lists the image once it is posted, then checks the call succeeds, the SysImageInTag POST carrying the image comes before the CreateTheme call, and the CreateTheme body carries the image background.")]
	[Description("Registers a theme background image in the background gallery before CreateTheme through the real MCP server, then writes the theme with the image background.")]
	public Task CreateTheme_Should_Register_The_Image_In_The_Gallery_Before_The_Theme_Write() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", GalleryStub(listsRegistration: true),
			async (session, environmentName, stubServer, cancellationToken) => {
				CreateThemeResult result = await CreateThemeWithImageAsync(session, environmentName, cancellationToken);
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);

				result.Success.Should().BeTrue(
					because: $"an image the gallery takes may become the theme background (error: {result.Error})");
				int registration = GalleryRegistrationIndex(requests);
				registration.Should().BeGreaterThanOrEqualTo(0, because: "the image is posted to the background gallery");
				registration.Should().BeLessThan(ThemeWriteIndex(requests, "CreateTheme"),
					because: "the image joins the background gallery before the theme write");
				ThemeWriteBody(requests, "CreateTheme").Should().Contain(ImageBackground,
					because: "the image background must reach ThemeService in the CreateTheme call itself");
			});

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme refuses the theme write when the background gallery does not take the image")]
	[AllureDescription("Calls create-theme with background-image-id through the real MCP server against a 10.2.96 stub whose background gallery accepts the membership POST but never lists it, then checks the call is refused because the registration failed, the theme is reported as not written, and no CreateTheme call reached the stub.")]
	[Description("Refuses the theme write through the real MCP server when the background gallery does not list the image after its registration.")]
	public Task CreateTheme_Should_Refuse_The_Theme_Write_When_The_Gallery_Does_Not_Take_The_Image() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", GalleryStub(listsRegistration: false),
			async (session, environmentName, stubServer, cancellationToken) => {
				CreateThemeResult result = await CreateThemeWithImageAsync(session, environmentName, cancellationToken);
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);

				result.Success.Should().BeFalse(because: "a theme background must name an image the background gallery holds");
				result.Error.Should().StartWith("Registering the image in the background gallery failed.")
					.And.EndWith("The theme was not written.",
						because: "the caller must learn the gallery refused the image and that the theme was not written");
				GalleryRegistrationIndex(requests).Should().BeGreaterThanOrEqualTo(0,
					because: "the image is posted to the background gallery before it is refused");
				requests.Should().NotContain(request => request.Url.EndsWith("/ThemeService.svc/CreateTheme"),
					because: "a gallery that does not take the image stops the theme write");
			});

	[Test]
	[AllureTag(UpdateThemeTool.ToolName)]
	[AllureName("update-theme registers the background image in the background gallery before the theme write")]
	[AllureDescription("Calls update-theme with only id, background-image-id and keep-icon-background through the real MCP server against a 10.2.96 stub that lists the theme in its catalog and whose background gallery lists the image once it is posted, then checks the call succeeds, the SysImageInTag POST comes before the UpdateTheme call, and the UpdateTheme body carries the image background.")]
	[Description("Registers a theme background image in the background gallery before UpdateTheme through the real MCP server, then writes the image background.")]
	public Task UpdateTheme_Should_Register_The_Image_In_The_Gallery_Before_The_Theme_Write() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e",
			GalleryStub(listsRegistration: true) with { ThemeCatalogJson = CatalogJson },
			async (session, environmentName, stubServer, cancellationToken) => {
				CommandExecutionEnvelope result = McpCommandExecutionParser.Extract(
					await session.CallToolAsync(UpdateThemeTool.ToolName, new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["id"] = ThemeId,
							["background-image-id"] = ImageId,
							["keep-icon-background"] = true
						}
					}, cancellationToken));
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);

				result.ExitCode.Should().Be(0, because: "an image the gallery takes may become the theme background");
				int registration = GalleryRegistrationIndex(requests);
				registration.Should().BeGreaterThanOrEqualTo(0, because: "the image is posted to the background gallery");
				registration.Should().BeLessThan(ThemeWriteIndex(requests, "UpdateTheme"),
					because: "the image joins the background gallery before the theme write");
				ThemeWriteBody(requests, "UpdateTheme").Should().Contain(ImageBackground,
					because: "the image background must reach ThemeService in the UpdateTheme call itself");
			});

	[Test]
	[AllureTag(UpdateThemeTool.ToolName)]
	[AllureName("update-theme writes only the background")]
	[AllureDescription("Calls update-theme with background-color and keep-icon-background and no content fields through the real MCP server against a 10.2.96 stub, then checks the log reports the background and the kept feature with no warning, and the UpdateTheme body carries the background and no content field.")]
	[Description("Changes only the background of a theme through the real MCP server against a 10.2.96 stub, without caption, css-class-name or css-content.")]
	public Task UpdateTheme_Should_Write_Only_The_Background() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", Stub("10.2.96.0"),
			async (session, environmentName, stubServer, cancellationToken) => {
				CommandExecutionEnvelope result = McpCommandExecutionParser.Extract(
					await session.CallToolAsync(UpdateThemeTool.ToolName, new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["id"] = ThemeId,
							["background-color"] = "#1A2B3C",
							["keep-icon-background"] = true
						}
					}, cancellationToken));
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);
				string body = ThemeWriteBody(requests, "UpdateTheme");

				result.ExitCode.Should().Be(0, because: "a background-only update is a valid call");
				result.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info
						&& message.Value == "Theme background: Color #1A2B3C.",
					because: "the log must say which background was written");
				result.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info
						&& message.Value == "UsePanelIconBackground was left as it is (keep-icon-background).",
					because: "the log must say the panel icon feature was kept as the caller asked");
				result.Output.Should().NotContain(message => message.MessageType == LogDecoratorType.Warning,
					because: "nothing failed after the theme write");
				body.Should().Contain(ColorBackground,
					because: "the background must reach ThemeService in the UpdateTheme call itself");
				body.Should().NotContainAny(ContentKeys,
					because: "an omitted content field must be left out so the stored value is kept");
			});

	[TestCase("background-image-id")]
	[TestCase("background-color")]
	[AllureTag(UpdateThemeTool.ToolName)]
	[AllureName("update-theme removes the background when a background argument is null")]
	[AllureDescription("Calls update-theme with a JSON null background-image-id or background-color and no content fields through the real MCP server against a 10.2.96 stub, then checks the log reports the removal with no warning and the UpdateTheme body sends an empty background and no content field.")]
	[Description("Removes the background of a theme through the real MCP server against a 10.2.96 stub when a background argument is a JSON null, without caption, css-class-name or css-content.")]
	public Task UpdateTheme_Should_Remove_The_Background_When_A_Background_Argument_Is_Null(string argument) =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", Stub("10.2.96.0"),
			async (session, environmentName, stubServer, cancellationToken) => {
				CommandExecutionEnvelope result = McpCommandExecutionParser.Extract(
					await session.CallToolAsync(UpdateThemeTool.ToolName, new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["id"] = ThemeId,
							[argument] = null
						}
					}, cancellationToken));
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);
				string body = ThemeWriteBody(requests, "UpdateTheme");

				result.ExitCode.Should().Be(0, because: "a removal alone is a valid call");
				result.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info
						&& message.Value == "The theme has no background now.",
					because: "the log must say the background was removed");
				result.Output.Should().NotContain(message => message.MessageType == LogDecoratorType.Warning,
					because: "a removal has no step after the theme write that can fail");
				body.Should().Contain("\"background\":{}",
					because: "an empty background is what tells ThemeService to remove the stored one");
				body.Should().NotContainAny(ContentKeys,
					because: "an omitted content field must be left out so the stored value is kept");
			});

	[TestCase(CreateThemeTool.ToolName, "CreateTheme")]
	[TestCase(UpdateThemeTool.ToolName, "UpdateTheme")]
	[AllureTag(CreateThemeTool.ToolName, UpdateThemeTool.ToolName)]
	[AllureName("A theme background is refused below Creatio 10.2.96")]
	[AllureDescription("Calls create-theme and update-theme with a background through the real MCP server against a 10.2.95 stub, then checks the call is refused with version-too-old and no theme write reached the stub.")]
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

	private static string ThemeWriteBody(IReadOnlyList<RecordedStubRequest> requests, string operation) =>
		requests.Should().ContainSingle(request => request.Url.EndsWith("/ThemeService.svc/" + operation),
			because: $"exactly one {operation} call writes the theme").Which.Body ?? string.Empty;

	private static RuntimeDetectionStubServerConfiguration GalleryStub(bool listsRegistration) {
		return Stub("10.2.96.0") with {
			BackgroundGalleryImageId = ImageId,
			BackgroundGalleryListsRegistration = listsRegistration
		};
	}

	private static async Task<CreateThemeResult> CreateThemeWithImageAsync(McpServerSession session,
		string environmentName, CancellationToken cancellationToken) {
		CallToolResult callResult = await session.CallToolAsync(CreateThemeTool.ToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName,
					["caption"] = "Gallery probe",
					["css-content"] = ".gallery-probe{color:#003366}",
					["background-image-id"] = ImageId,
					["keep-icon-background"] = true
				}
			}, cancellationToken);
		return EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(callResult);
	}

	private static int GalleryRegistrationIndex(IReadOnlyList<RecordedStubRequest> requests) {
		return requests.ToList().FindIndex(request => request.Method == "POST"
			&& request.Url.EndsWith("/odata/SysImageInTag") && (request.Body ?? string.Empty).Contains(ImageId));
	}

	private static int ThemeWriteIndex(IReadOnlyList<RecordedStubRequest> requests, string operation) {
		return requests.ToList().FindIndex(request => request.Url.EndsWith("/ThemeService.svc/" + operation));
	}
}
