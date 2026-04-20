using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[AllureFeature("mcp-guidance-resources")]
[NonParallelizable]
public sealed class McpGuidanceResourceE2ETests {
	private const string DocsScheme = "docs";
	private const string GuidesPath = "mcp/guides";
	private static readonly string AppModelingUri = BuildGuideUri("app-modeling");
	private static readonly string ExistingAppMaintenanceUri = BuildGuideUri("existing-app-maintenance");
	private static readonly string PageSchemaHandlersUri = BuildGuideUri("page-schema-handlers");
	private static readonly string PageSchemaConvertersUri = BuildGuideUri("page-schema-converters");
	private static readonly string PageSchemaValidatorsUri = BuildGuideUri("page-schema-validators");

	[Test]
	[AllureTag("mcp-guidance-resources")]
	[AllureName("MCP server advertises modeling and existing-app guidance resources")]
	public async Task McpServer_Should_Advertise_Guidance_Resources() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientResource> resources = await context.Session.ListResourcesAsync(context.CancellationTokenSource.Token);

		// Assert
		resources.Select(resource => resource.Uri).Should().Contain([
				AppModelingUri,
				ExistingAppMaintenanceUri,
				PageSchemaHandlersUri,
				PageSchemaConvertersUri,
				PageSchemaValidatorsUri
			],
			because: "the MCP server should advertise creation existing-app and page-section guidance resources");
	}

	[Test]
	[AllureTag("mcp-guidance-resources")]
	[AllureName("MCP server returns the existing-app maintenance guidance article")]
	public async Task McpServer_Should_Return_Existing_App_Maintenance_Guidance() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ReadResourceResult result = await context.Session.ReadResourceAsync(ExistingAppMaintenanceUri, context.CancellationTokenSource.Token);

		// Assert
		result.Contents.Should().ContainSingle(
			because: "the guidance resource should resolve to a single plain-text article");
		TextResourceContents article = result.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the maintenance guide should be returned as plain text").Subject;
		article.Uri.Should().Be(ExistingAppMaintenanceUri,
			because: "the returned article should preserve the stable maintenance guidance URI");
		article.Text.Should().Contain("list-apps",
			because: "the article should explain how to discover the target installed application");
		article.Text.Should().Contain("update-page",
			because: "the article should describe the minimal page mutation path");
		article.Text.Should().Contain("do not wrap MCP arguments inside `args`",
			because: "the article should explicitly reject the request wrapper that caused the analyzed session failure");
		article.Text.Should().Contain("do not send `bundle` or `bundle.viewConfig` as the body payload",
			because: "the article should explain the concrete writable page payload shape");
		article.Text.Should().Contain("JSON object string",
			because: "the article should explain the concrete page resources payload shape");
		article.Text.Should().Contain("create-data-binding-db",
			because: "the article should steer standalone lookup seeding to MCP-native data-binding tools");
		article.Text.Should().Contain("modify-entity-schema-column",
			because: "the article should describe the minimal single-column schema mutation path");
		article.Text.Should().Contain("Read before write",
			because: "the article should encode the canonical maintenance verification discipline");
	}

	[Test]
	[AllureTag("mcp-guidance-resources")]
	[AllureName("MCP server returns the page-schema handlers guidance article")]
	public async Task McpServer_Should_Return_Page_Schema_Handlers_Guidance() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ReadResourceResult result = await context.Session.ReadResourceAsync(PageSchemaHandlersUri, context.CancellationTokenSource.Token);

		// Assert
		TextResourceContents article = result.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the handlers guide should resolve to a single plain-text article").Subject;
		article.Uri.Should().Be(PageSchemaHandlersUri,
			because: "the returned article should preserve the stable handler guidance URI");
		article.Text.Should().Contain("request.$context.executeRequest(...)",
			because: "the handler guide should document the canonical nested-request entry point");
		article.Text.Should().Contain("next?.handle(request)",
			because: "the handler guide should document how request-chain continuation works");
		article.Text.Should().Contain("SCHEMA_HANDLERS",
			because: "the handler guide should anchor editing to the correct page-body marker section");
		article.Text.Should().Contain("@CrtRequestHandler",
			because: "the handler guide should mention the public frontend-source registration pattern");
		article.Text.Should().Contain("Do not use a handler for pure display transformation tasks",
			because: "the handler guide should explicitly reject uppercase-label scenarios that belong to converters");
	}

	[Test]
	[AllureTag("mcp-guidance-resources")]
	[AllureName("MCP server returns the page-schema converters guidance article")]
	public async Task McpServer_Should_Return_Page_Schema_Converters_Guidance() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ReadResourceResult result = await context.Session.ReadResourceAsync(PageSchemaConvertersUri, context.CancellationTokenSource.Token);

		// Assert
		TextResourceContents article = result.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the converters guide should resolve to a single plain-text article").Subject;
		article.Uri.Should().Be(PageSchemaConvertersUri,
			because: "the returned article should preserve the stable converter guidance URI");
		article.Text.Should().Contain("SCHEMA_CONVERTERS",
			because: "the converter guide should anchor editing to the correct page-body marker section");
		article.Text.Should().Contain("value transformation",
			because: "the converter guide should state the intended responsibility of converters");
		article.Text.Should().Contain("usr.ToUpperCase",
			because: "the converter guide should provide a concrete Academy-style example");
		article.Text.Should().Contain("$UsrName | usr.ToUpperCase",
			because: "the converter guide should show the expected binding shape for uppercase display labels");
		article.Text.Should().Contain("UsrUppercaseNameLabel",
			because: "the converter guide should include the cookbook label insertion example for uppercase display scenarios");
		article.Text.Should().Contain("implement-the-field-value-conversion",
			because: "the converter guide should link to the Academy field-value conversion example");
		article.Text.Should().Contain("get-guidance",
			because: "the converter guide should redirect request-chain logic through the guidance tool");
	}

	[Test]
	[AllureTag("mcp-guidance-resources")]
	[AllureName("MCP server returns the page-schema validators guidance article")]
	public async Task McpServer_Should_Return_Page_Schema_Validators_Guidance() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ReadResourceResult result = await context.Session.ReadResourceAsync(PageSchemaValidatorsUri, context.CancellationTokenSource.Token);

		// Assert
		TextResourceContents article = result.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the validators guide should resolve to a single plain-text article").Subject;
		article.Uri.Should().Be(PageSchemaValidatorsUri,
			because: "the returned article should preserve the stable validator guidance URI");
		article.Text.Should().Contain("SCHEMA_VALIDATORS",
			because: "the validator guide should anchor editing to the correct page-body marker section");
		article.Text.Should().Contain("field-value validation",
			because: "the validator guide should state the intended responsibility of validators");
		article.Text.Should().Contain("@CrtValidator",
			because: "the validator guide should mention the public frontend-source registration pattern");
		article.Text.Should().Contain("crt.MaxLength",
			because: "the validator guide should publish the built-in max-length validator in the standard decision table");
		article.Text.Should().Contain("Do NOT create a custom validator when a standard validator is sufficient",
			because: "the validator guide should explicitly prevent unnecessary custom validators for standard cases");
		article.Text.Should().Contain("get-guidance",
			because: "the validator guide should redirect dynamic UI-state logic through the guidance tool");
	}

	private static async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private static string BuildGuideUri(string guideName) => $"{DocsScheme}://{GuidesPath}/{guideName}";

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
