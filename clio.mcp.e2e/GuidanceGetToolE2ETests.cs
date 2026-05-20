using System;
using System.Collections.Generic;
using System.Linq;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

[TestFixture]
[AllureNUnit]
[AllureFeature(GuidanceGetTool.ToolName)]
[NonParallelizable]
public sealed class GuidanceGetToolE2ETests {
	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance tool is advertised by the MCP server")]
	public async Task GuidanceGet_Should_Be_Listed_By_Mcp_Server() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		IList<McpClientTool> tools = await context.Session.ListToolsAsync(context.CancellationTokenSource.Token);

		// Assert
		tools.Select(tool => tool.Name).Should().Contain(GuidanceGetTool.ToolName,
			because: "the MCP server should advertise get-guidance as the tool-native way to read canonical guidance");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical handler guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Handlers_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-handlers"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-handlers is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-handlers",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP page-schema handlers guide",
			because: "the guidance tool should return the canonical handler guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical validator guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Validators_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-validators"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-validators is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-validators",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP page-schema validators guide",
			because: "the guidance tool should return the canonical validator guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical sdk common guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Sdk_Common_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-creatio-devkit-common"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-creatio-devkit-common is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-creatio-devkit-common",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP page-schema sdk common guide",
			because: "the guidance tool should return the canonical sdk common guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical page localizable string guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Resources_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-resources"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-resources is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-resources",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP page-schema resources guide",
			because: "the guidance tool should return the canonical resources guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical converter guidance article")]
	public async Task GuidanceGet_Should_Return_Page_Schema_Converters_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "page-schema-converters"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "page-schema-converters is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/page-schema-converters",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("clio MCP page-schema converters guide",
			because: "the guidance tool should return the canonical converter guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical configuration web-service guide")]
	public async Task GuidanceGet_Should_Return_Configuration_WebService_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "configuration-webservice"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "configuration-webservice is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/configuration-webservice",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("creatio-config-webservice",
			because: "the guidance tool should return the canonical configuration web-service guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the canonical configuration web-service test guide")]
	public async Task GuidanceGet_Should_Return_Configuration_WebService_Tests_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "configuration-webservice-tests"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "configuration-webservice-tests is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/configuration-webservice-tests",
			because: "the canonical resource URI should still be visible in the tool response");
		response.Article.Text.Should().Contain("configuration-webservice-tests",
			because: "the guidance tool should return the canonical configuration web-service test guide text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns generated composable-app skill guides")]
	public async Task GuidanceGet_Should_Return_Generated_Composable_App_Skill_Guides() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse atfResponse = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "atf-repository-dev"
			});
		GuidanceGetResponse sysSettingTestsResponse = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "sys-setting-tests"
			});

		// Assert
		atfResponse.Success.Should().BeTrue(
			because: "atf-repository-dev is a generated guidance name");
		atfResponse.Article.Should().NotBeNull(
			because: "successful generated guidance lookups should return the resolved article payload");
		atfResponse.Article!.Uri.Should().Be("docs://mcp/guides/atf-repository-dev",
			because: "the canonical generated resource URI should still be visible in the tool response");
		atfResponse.Article.Text.Should().Contain("ATF.Repository",
			because: "the guidance tool should return the generated source skill text");

		sysSettingTestsResponse.Success.Should().BeTrue(
			because: "sys-setting-tests is a generated guidance name");
		sysSettingTestsResponse.Article.Should().NotBeNull(
			because: "successful generated test guidance lookups should return the resolved article payload");
		sysSettingTestsResponse.Article!.Uri.Should().Be("docs://mcp/guides/sys-setting-tests",
			because: "the canonical generated test resource URI should still be visible in the tool response");
		sysSettingTestsResponse.Article.Text.Should().Contain("SetupSysSettings",
			because: "the guidance tool should return the generated test skill text");
	}

	[Test]
	[AllureTag(GuidanceGetTool.ToolName)]
	[AllureName("get-guidance returns the mobile page modification guide")]
	[Description("Verifies that get-guidance returns the mobile-page-modification article with correct URI and constraint text about validators and handlers.")]
	public async Task GuidanceGet_Should_Return_Mobile_Page_Modification_Guide() {
		// Arrange
		McpE2ESettings settings = TestConfiguration.Load();
		settings.ClioProcessPath = TestConfiguration.ResolveFreshClioProcessPath();
		await using ArrangeContext context = await ArrangeAsync(settings, TimeSpan.FromMinutes(3));

		// Act
		GuidanceGetResponse response = await CallAsync(
			context.Session,
			context.CancellationTokenSource.Token,
			new Dictionary<string, object?> {
				["name"] = "mobile-page-modification"
			});

		// Assert
		response.Success.Should().BeTrue(
			because: "mobile-page-modification is a registered guidance name");
		response.Article.Should().NotBeNull(
			because: "successful guidance lookups should return the resolved article payload");
		response.Article!.Uri.Should().Be("docs://mcp/guides/mobile-page-modification",
			because: "the canonical resource URI for the mobile page guide should be stable");
		response.Article.Text.Should().Contain("clio MCP mobile page modification guide",
			because: "the guidance tool should return the canonical mobile page guide text");
		response.Article.Text.Should().Contain("validators",
			because: "the mobile guide must document that validators are not supported in mobile pages");
		response.Article.Text.Should().Contain("handlers",
			because: "the mobile guide must document that handlers are not supported in mobile pages");
	}

	private static async Task<ArrangeContext> ArrangeAsync(McpE2ESettings settings, TimeSpan timeout) {
		CancellationTokenSource cancellationTokenSource = new(timeout);
		McpServerSession session = await McpServerSession.StartAsync(settings, cancellationTokenSource.Token);
		return new ArrangeContext(session, cancellationTokenSource);
	}

	private static async Task<GuidanceGetResponse> CallAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, object?> arguments) {
		CallToolResult callResult = await session.CallToolAsync(
			GuidanceGetTool.ToolName,
			new Dictionary<string, object?> { ["args"] = arguments },
			cancellationToken);
		callResult.IsError.Should().NotBeTrue(
			because: "get-guidance should return a normal MCP tool result envelope for valid request shapes");
		return EntitySchemaStructuredResultParser.Extract<GuidanceGetResponse>(callResult);
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
