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
	private static readonly string DataBindingsUri = BuildGuideUri("data-bindings");
	private static readonly string ExistingAppMaintenanceUri = BuildGuideUri("existing-app-maintenance");
	private static readonly string PageSchemaValidatorsUri = BuildGuideUri("page-schema-validators");

	[Test]
	[AllureTag("mcp-guidance-resources")]
	[AllureName("MCP server advertises modeling, binding, existing-app, and validator guidance resources")]
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
				DataBindingsUri,
				ExistingAppMaintenanceUri,
				PageSchemaValidatorsUri
			],
			because: "the MCP server should advertise creation, generic binding, existing-app, and validator guidance resources");
	}

	[Test]
	[AllureTag("mcp-guidance-resources")]
	[AllureName("MCP server returns the data-bindings guidance article")]
	public async Task McpServer_Should_Return_Data_Bindings_Guidance() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ReadResourceResult result = await context.Session.ReadResourceAsync(DataBindingsUri, context.CancellationTokenSource.Token);

		// Assert
		TextResourceContents article = result.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the binding guide should resolve to a single plain-text article").Subject;
		article.Uri.Should().Be(DataBindingsUri,
			because: "the returned article should preserve the stable binding guidance URI");
		article.Text.Should().Contain("sync-schemas",
			because: "the guide should advertise the canonical batched lookup-seeding path");
		article.Text.Should().Contain("create-data-binding-db",
			because: "the guide should advertise the DB-first binding path");
		article.Text.Should().Contain("create-data-binding",
			because: "the guide should advertise the local binding artifact path");
		article.Text.Should().NotContain(".agents/skills/clio-data-bindings",
			because: "the guide should stay valid after install-skills copies the bundle outside the source repo layout");
		article.Text.Should().NotContain("assets/bindings-lookup.json",
			because: "section-specific stable ID sourcing does not belong in the generic binding guide");
		article.Text.Should().Contain("runtime-only columns are not blockers",
			because: "the guide should explain the DB-first subset-column projection rule for Account-like schemas");
		article.Text.Should().Contain("install logs or planned payloads",
			because: "the guide should reject install-log-only verification for remote binding mutations");
		article.Text.Should().Contain("Seed rows do not implement defaults",
			because: "the guide should keep lookup seeding separate from default semantics");
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
		article.Text.Should().Contain("setAttributePropertyValue(...)",
			because: "the validator guide should redirect dynamic UI-state logic away from validators without pointing to removed handler or converter guides");
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
