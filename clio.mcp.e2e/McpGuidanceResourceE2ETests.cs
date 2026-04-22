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
	private const string AppModelingUri = "docs://mcp/guides/app-modeling";
	private const string ExistingAppMaintenanceUri = "docs://mcp/guides/existing-app-maintenance";
	private const string ObjectBusinessRulesUri = "docs://mcp/guides/object-business-rules";

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
				ObjectBusinessRulesUri
			],
			because: "the MCP server should advertise creation, maintenance, and business-rule guidance resources");
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
	[AllureName("MCP server returns the object business-rules guidance article")]
	public async Task McpServer_Should_Return_Object_Business_Rules_Guidance() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		ReadResourceResult result = await context.Session.ReadResourceAsync(ObjectBusinessRulesUri, context.CancellationTokenSource.Token);

		// Assert
		result.Contents.Should().ContainSingle(
			because: "the business-rule guidance resource should resolve to a single plain-text article");
		TextResourceContents article = result.Contents.Single().Should().BeOfType<TextResourceContents>(
			because: "the business-rule guide should be returned as plain text").Subject;
		article.Uri.Should().Be(ObjectBusinessRulesUri,
			because: "the returned article should preserve the stable business-rule guidance URI");
		article.Text.Should().Contain("create-entity-business-rule",
			because: "the article should point callers to the current business-rule creation tool");
		article.Text.Should().Contain("get-entity-schema-properties",
			because: "the article should require schema inspection before rule authoring");
		article.Text.Should().Contain("`equal`, `not-equal`, `is-filled-in`, `is-not-filled-in`, `greater-than`, `greater-than-or-equal`, `less-than`, and `less-than-or-equal`",
			because: "the article should enumerate the expanded comparison subset");
		article.Text.Should().Contain("rightExpression` is omitted or null for `is-filled-in` and `is-not-filled-in`",
			because: "the article should explain the unary operand rule");
		article.Text.Should().Contain("Temporal scope is `Date`, `DateTime`, and `Time`",
			because: "the article should scope relational temporal support precisely");
		article.Text.Should().Contain("raw GUID string",
			because: "the article should teach the lookup-constant payload constraint");
		article.Text.Should().Contain("dataforge-find-lookups",
			because: "the article should include the DataForge lookup-resolution path for lookup constants");
		article.Text.Should().Contain("create-only",
			because: "the article should state that the current MCP lifecycle is limited to rule creation");
	}

	private static async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private sealed record ArrangeContext(
		McpServerSession Session,
		CancellationTokenSource CancellationTokenSource) : IAsyncDisposable {
		public async ValueTask DisposeAsync() {
			await Session.DisposeAsync();
			CancellationTokenSource.Dispose();
		}
	}
}
