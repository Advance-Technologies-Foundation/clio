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

	private const string ThemePackageName = "UsrBranding";

	private const string ColorBackground = "\"background\":{\"mode\":\"Color\",\"color\":\"#1A2B3C\"}";

	private const string PackageDeliveryWarning = "The theme background was applied, but binding it into ";

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
	[AllureDescription("Calls create-theme with background-color and keep-icon-background through the real MCP server against a 10.2.96 stub, then checks the result reports the panel icon background as kept, the only warning is the package delivery the stub does not serve, and the CreateTheme body carries the background.")]
	[Description("Creates a theme with a background color through the real MCP server against a 10.2.96 stub.")]
	public Task CreateTheme_Should_Write_The_Background() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", Stub("10.2.96.0"),
			async (session, environmentName, stubServer, cancellationToken) => {
				// Act
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

				// Assert
				result.Success.Should().BeTrue(
					because: $"a theme and its background are written by one call (error: {result.Error})");
				result.PanelIconBackground.Should().Be(ThemeBackgroundOutcome.PanelIconKept,
					because: "keep-icon-background leaves the UsePanelIconBackground feature as it is");
				result.Warnings.Should().ContainSingle(because: "a color background is delivered into the theme's package too")
					.Which.Should().StartWith(PackageDeliveryWarning,
						because: "the stub serves no package delivery, so only that step can fail after the theme write");
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
				// Act
				CreateThemeResult result = await CreateThemeWithImageAsync(session, environmentName, cancellationToken);
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);

				// Assert
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
				// Act
				CreateThemeResult result = await CreateThemeWithImageAsync(session, environmentName, cancellationToken);
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);

				// Assert
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
				// Act
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

				// Assert
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
	[AllureName("update-theme leaves the background gallery alone for a theme in a locked package")]
	[AllureDescription("Calls update-theme with id and background-image-id through the real MCP server against a 10.2.96 stub that lists the theme in its catalog and reports the theme's package as locked, then checks the call is refused naming the locked package, and neither a SysImageInTag POST nor an UpdateTheme call reached the stub.")]
	[Description("Refuses an image background through the real MCP server for a theme whose package is locked, before the image joins the background gallery.")]
	public Task UpdateTheme_Should_Leave_The_Gallery_Alone_When_The_Theme_Package_Is_Locked() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e",
			GalleryStub(listsRegistration: true) with { ThemeCatalogJson = CatalogJson, ThemePackageLocked = true },
			async (session, environmentName, stubServer, cancellationToken) => {
				// Act
				CommandExecutionEnvelope result = McpCommandExecutionParser.Extract(
					await session.CallToolAsync(UpdateThemeTool.ToolName, new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["id"] = ThemeId,
							["background-image-id"] = ImageId
						}
					}, cancellationToken));
				string output = string.Join(" ", result.Output.Select(message => message.Value));
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);

				// Assert
				result.ExitCode.Should().Be(1, because: "a locked package refuses the theme write");
				output.Should().Contain($"The theme's package '{ThemePackageName}' is locked. The theme was not written.")
					.And.NotContainEquivalentOf("unlock",
						because: "the caller learns the package is locked and nothing was written, without advice to unlock it");
				GalleryRegistrationIndex(requests).Should().Be(-1,
					because: "the image must not join the environment-wide gallery for a theme that cannot be written");
				requests.Should().NotContain(request => request.Url.EndsWith("/ThemeService.svc/UpdateTheme"),
					because: "a locked package stops the theme write before it is sent");
			});

	[Test]
	[AllureTag(UpdateThemeTool.ToolName)]
	[AllureName("update-theme writes only the background")]
	[AllureDescription("Calls update-theme with background-color and keep-icon-background and no content fields through the real MCP server against a 10.2.96 stub that lists the theme in its catalog, then checks the log reports the background and the kept feature, the only warning is the package delivery the stub does not serve, and the UpdateTheme body carries the background and no content field.")]
	[Description("Changes only the background of a theme through the real MCP server against a 10.2.96 stub, without caption, css-class-name or css-content.")]
	public Task UpdateTheme_Should_Write_Only_The_Background() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", Stub("10.2.96.0") with { ThemeCatalogJson = CatalogJson },
			async (session, environmentName, stubServer, cancellationToken) => {
				// Act
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

				// Assert
				result.ExitCode.Should().Be(0, because: "a background-only update is a valid call");
				result.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info
						&& message.Value == "Theme background: Color #1A2B3C.",
					because: "the log must say which background was written");
				result.Output.Should().Contain(message => message.MessageType == LogDecoratorType.Info
						&& message.Value == "UsePanelIconBackground was left as it is (keep-icon-background).",
					because: "the log must say the panel icon feature was kept as the caller asked");
				result.Output.Where(message => message.MessageType == LogDecoratorType.Warning).Should()
					.ContainSingle(because: "a color background is delivered into the theme's package too")
					.Which.Value.Should().StartWith(PackageDeliveryWarning,
						because: "the stub serves no package delivery, so only that step can fail after the theme write");
				body.Should().Contain(ColorBackground,
					because: "the background must reach ThemeService in the UpdateTheme call itself");
				body.Should().NotContainAny(ContentKeys,
					because: "an omitted content field must be left out so the stored value is kept");
			});

	[Test]
	[AllureTag(UpdateThemeTool.ToolName)]
	[AllureName("update-theme ignores a JSON null in both background arguments")]
	[AllureDescription("Calls update-theme with caption, css-class-name and css-content and a JSON null for both background arguments, as a client that serializes every optional field sends it, through the real MCP server against a 10.2.95 stub below the background floor, then checks the call succeeds as a plain overwrite whose UpdateTheme body carries the content and no background.")]
	[Description("Runs a plain overwrite whose client sends JSON null for both background arguments, even below the background floor, and sends no background.")]
	public Task UpdateTheme_Should_Ignore_Null_In_Both_Background_Arguments() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", Stub("10.2.95.0"),
			async (session, environmentName, stubServer, cancellationToken) => {
				// Act
				CommandExecutionEnvelope result = McpCommandExecutionParser.Extract(
					await session.CallToolAsync(UpdateThemeTool.ToolName, new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["id"] = ThemeId,
							["caption"] = "Overwrite probe",
							["css-class-name"] = "overwrite-probe",
							["css-content"] = ".overwrite-probe{color:#003366}",
							["background-image-id"] = null,
							["background-color"] = null,
							["keep-icon-background"] = null
						}
					}, cancellationToken));
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);
				string body = ThemeWriteBody(requests, "UpdateTheme");

				// Assert
				result.ExitCode.Should().Be(0, because: "a null in every unused optional argument is a plain overwrite");
				body.Should().ContainAll(ContentKeys, because: "a plain overwrite sends every content field");
				body.Should().NotContain("\"background\"",
					because: "a null the client did not mean must not remove the stored background");
			});

	[Test]
	[AllureTag(UpdateThemeTool.ToolName)]
	[AllureName("update-theme writes a color next to a JSON null image id")]
	[AllureDescription("Calls update-theme with background-color and a JSON null background-image-id, as a client that serializes every optional field sends it, through the real MCP server against a 10.2.96 stub that lists the theme, then checks the UpdateTheme body carries the color background.")]
	[Description("Writes the color background when the client sends a JSON null for the unused background-image-id.")]
	public Task UpdateTheme_Should_Write_A_Color_Next_To_A_Null_Image_Id() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", Stub("10.2.96.0") with { ThemeCatalogJson = CatalogJson },
			async (session, environmentName, stubServer, cancellationToken) => {
				// Act
				CommandExecutionEnvelope result = McpCommandExecutionParser.Extract(
					await session.CallToolAsync(UpdateThemeTool.ToolName, new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["id"] = ThemeId,
							["background-image-id"] = null,
							["background-color"] = "#1A2B3C",
							["keep-icon-background"] = true
						}
					}, cancellationToken));
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);

				// Assert
				result.ExitCode.Should().Be(0, because: "a null next to a value is the client's unused argument");
				ThemeWriteBody(requests, "UpdateTheme").Should().Contain(ColorBackground,
					because: "the color the caller set must reach ThemeService");
			});

	[Test]
	[AllureTag(CreateThemeTool.ToolName)]
	[AllureName("create-theme turns the panel icon background off after the theme write when it is not kept")]
	[AllureDescription("Calls create-theme with background-color and without keep-icon-background through the real MCP server against a 10.2.96 stub that serves no feature-state write, then checks the theme is created with the background, the UsePanelIconBackground turn-off runs after the CreateTheme call, and its failure comes back as the first warning with no panel icon state.")]
	[Description("Turns the panel icon background off after the theme write when keep-icon-background is not set, and reports a failed turn-off as a warning without undoing the theme.")]
	public Task CreateTheme_Should_Turn_The_Panel_Icon_Background_Off_When_It_Is_Not_Kept() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", Stub("10.2.96.0"),
			async (session, environmentName, stubServer, cancellationToken) => {
				// Act
				CreateThemeResult result = EntitySchemaStructuredResultParser.Extract<CreateThemeResult>(
					await session.CallToolAsync(CreateThemeTool.ToolName, new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["caption"] = "Panel icon probe",
							["css-content"] = ".panel-icon-probe{color:#003366}",
							["background-color"] = "#1A2B3C"
						}
					}, cancellationToken));
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);

				// Assert
				result.Success.Should().BeTrue(
					because: $"a failure after the theme write does not undo the theme (error: {result.Error})");
				ThemeWriteBody(requests, "CreateTheme").Should().Contain(ColorBackground,
					because: "the background must reach ThemeService in the CreateTheme call itself");
				int createIndex = ThemeWriteIndex(requests, "CreateTheme");
				requests.Skip(createIndex + 1).Should().Contain(request => request.Url.Contains("SelectQuery")
						&& (request.Body ?? string.Empty).Contains("AppFeature"),
					because: "the UsePanelIconBackground turn-off reads the feature after the theme write");
				result.PanelIconBackground.Should().BeNull(
					because: "the stub serves no feature-state write, so the feature state is unknown");
				result.Warnings.Should().NotBeNullOrEmpty(because: "the failed turn-off must reach the caller");
				result.Warnings![0].Should().StartWith(
					"The theme background was applied, but turning off the UsePanelIconBackground feature failed",
					because: "without keep-icon-background the turn-off runs, and its failure is the first warning");
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
				// Act
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

				// Assert
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
				// Act
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
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);

				// Assert
				refusal.Should().Contain(CreatioVersionRequirementException.VersionTooOldCode,
					because: "an older ThemeService would drop the background silently, so clio refuses it");
				requests.Should().NotContain(request => request.Url.EndsWith("/ThemeService.svc/" + operation),
					because: "the floor is checked before any write");
			});

	[Test]
	[AllureTag(UpdateThemeTool.ToolName)]
	[AllureName("A theme background removal is refused below Creatio 10.2.96")]
	[AllureDescription("Calls update-theme with the theme id and a JSON null in background-color alone through the real MCP server against a 10.2.95 stub, then checks the removal is refused with version-too-old and no UpdateTheme call reached the stub.")]
	[Description("Refuses a background removal by a lone JSON null below 10.2.96 with version-too-old, before any theme write.")]
	public Task UpdateTheme_Should_Refuse_A_Background_Removal_Below_The_Floor() =>
		StubEnvironmentStand.RunAsync("clio-theme-background-e2e", Stub("10.2.95.0"),
			async (session, environmentName, stubServer, cancellationToken) => {
				// Act
				CallToolResult callResult = await session.CallToolAsync(UpdateThemeTool.ToolName,
					new Dictionary<string, object?> {
						["args"] = new Dictionary<string, object?> {
							["environment-name"] = environmentName,
							["id"] = ThemeId,
							["background-color"] = null
						}
					}, cancellationToken);
				string refusal = string.Join(" ",
					McpCommandExecutionParser.Extract(callResult).Output.Select(message => message.Value));
				IReadOnlyList<RecordedStubRequest> requests = await stubServer.GetRecordedRequestsAsync(cancellationToken);

				// Assert
				refusal.Should().Contain(CreatioVersionRequirementException.VersionTooOldCode,
					because: "a lone null removes the background, which an older ThemeService cannot do");
				requests.Should().NotContain(request => request.Url.EndsWith("/ThemeService.svc/UpdateTheme"),
					because: "the floor is checked before any write");
			});

	private static string ThemeWriteBody(IReadOnlyList<RecordedStubRequest> requests, string operation) =>
		requests.Should().ContainSingle(request => request.Url.EndsWith("/ThemeService.svc/" + operation),
			because: $"exactly one {operation} call writes the theme").Which.Body ?? string.Empty;

	private static RuntimeDetectionStubServerConfiguration GalleryStub(bool listsRegistration) {
		return Stub("10.2.96.0") with {
			BackgroundGalleryImageId = ImageId,
			BackgroundGalleryListsRegistration = listsRegistration,
			ThemePackageName = ThemePackageName
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
					["package-name"] = ThemePackageName,
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
