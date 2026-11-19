using System.Linq;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.UserEnvironment;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// update-page end to end, through the tool: the tool declares that a call sends one write, the save, and a save the
/// site refuses without processing is then reported as <c>diagnostic.side-effect: none</c>.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PageUpdateToolSideEffectTests {

	private const string SelectQueryUrl = "http://test/DataService/json/SyncReply/SelectQuery";
	private const string GetSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema";
	private const string SaveSchemaUrl = "http://test/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
	private const string SchemaUId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
	private const string SchemaName = "Test_FormPage";

	private const string ValidBody =
		"define(\"Test_FormPage\", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ { return { " +
		"viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/, " +
		"viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/, " +
		"modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/, " +
		"handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/, " +
		"converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/, " +
		"validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/ }; });";

	[TestCase(403, "", "none", TestName = "UpdatePage_ShouldReportSideEffectNone_WhenTheSaveIsRefusedWithAnEmpty403")]
	[TestCase(401, "<html>Sign in</html>", "none", TestName = "UpdatePage_ShouldReportSideEffectNone_WhenTheSaveIsRefusedWithA401Page")]
	[TestCase(403, "{\"success\":false,\"errorInfo\":{\"message\":\"No rights\"}}", "unknown",
		TestName = "UpdatePage_ShouldReportSideEffectUnknown_WhenA403CarriesTheDesignersEnvelope")]
	[TestCase(500, "", "unknown", TestName = "UpdatePage_ShouldReportSideEffectUnknown_WhenTheSaveIsAnswered500")]
	[Description("Through the tool: the save is update-page's one write, so only a refusal without processing proves nothing was saved")]
	public async Task UpdatePage_ShouldReportTheProvenSideEffect(int status, string body, string sideEffect) {
		// Arrange
		IApplicationClient client = KeepingClient(new SiteHttpResponse(status, body));
		PageUpdateTool tool = CreateTool(client);

		// Act
		PageUpdateResponse response = await tool.UpdatePage(new PageUpdateArgs(SchemaName, ValidBody));

		// Assert
		response.Success.Should().BeFalse();
		response.SiteResponse.Status.Should().Be(status);
		response.Diagnostic.SideEffect.Should().Be(sideEffect);
		System.Text.Json.JsonSerializer.Serialize(response).Should().Contain("\"diagnostic\":{");
	}

	[Test]
	[Description("The declaration holds: an update-page call sends exactly one request that is not a read, the save")]
	public async Task UpdatePage_ShouldSendExactlyOneWrite_TheSave() {
		// Arrange
		IApplicationClient client = KeepingClient(new SiteHttpResponse(403, ""));
		PageUpdateTool tool = CreateTool(client);

		// Act
		await tool.UpdatePage(new PageUpdateArgs(SchemaName, ValidBody));

		// Assert
		string[] readUrls = [SelectQueryUrl, GetSchemaUrl];
		string[] otherRequests = client.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name is nameof(IApplicationClient.ExecutePostRequest)
				or nameof(IStatusPreservingApplicationClient.SendKeepingStatus)
				or nameof(IApplicationClient.ExecuteNonReplayablePostRequest)
				or nameof(IApplicationClient.ExecutePatchRequest)
				or nameof(IApplicationClient.ExecuteDeleteRequest))
			.Select(call => call.GetArguments().OfType<string>().First(argument => argument.StartsWith("http")))
			.Where(url => !readUrls.Contains(url))
			.ToArray();
		otherRequests.Should().Equal([SaveSchemaUrl],
			because: "update-page declares one write per call; a second write would make a refusal of it no proof");
	}

	// A client that keeps the status: designer reads succeed, the save gets the given answer, selects find the page.
	private static IApplicationClient KeepingClient(SiteHttpResponse saveAnswer) {
		IApplicationClient client = Substitute.For<IApplicationClient, IStatusPreservingApplicationClient>();
		client.ExecutePostRequest(SelectQueryUrl, Arg.Is<string>(body => !body.Contains("byUId")), Arg.Any<int>(),
				Arg.Any<int>(), Arg.Any<int>())
			.Returns($$"""{"success": true, "rows": [{"UId": "{{SchemaUId}}"}]}""");
		((IStatusPreservingApplicationClient)client)
			.SendKeepingStatus(default, default, default, default, default, default, default)
			.ReturnsForAnyArgs(call => call.ArgAt<string>(1) == SaveSchemaUrl
				? saveAnswer
				: new SiteHttpResponse(200,
					$$"""{"success": true, "schema": {"body": "old body", "name": "{{SchemaName}}" } }"""));
		return client;
	}

	private static PageUpdateTool CreateTool(IApplicationClient client) {
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build("/DataService/json/SyncReply/SelectQuery").Returns(SelectQueryUrl);
		urls.Build(ServiceUrlBuilder.KnownRoute.GetClientUnitDesignerSchema).Returns(GetSchemaUrl);
		urls.Build(ServiceUrlBuilder.KnownRoute.SaveClientUnitDesignerSchema).Returns(SaveSchemaUrl);
		IPageDesignerHierarchyClient hierarchy = Substitute.For<IPageDesignerHierarchyClient>();
		hierarchy.GetDesignPackageUId(SchemaUId).Returns("test-pkg-uid");
		hierarchy.GetParentSchemas(SchemaUId, "test-pkg-uid").Returns([
			new PageDesignerHierarchySchema { UId = SchemaUId, Name = SchemaName, PackageUId = "test-pkg-uid" }
		]);
		ILogger logger = Substitute.For<ILogger>();
		PageUpdateCommand command = new(client, urls, logger, Substitute.For<IPageBaselineGuard>(),
			new PersistedResourceKeyReader(), hierarchy, viewConfigApplierFactory: () => Substitute.For<IJsonDiffApplier>());
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<PageUpdateCommand>(Arg.Any<PageUpdateOptions>()).Returns(command);
		return new PageUpdateTool(command, logger, resolver, Substitute.For<IMobileComponentInfoCatalog>(),
			Substitute.For<IComponentInfoCatalog>(), Substitute.For<IPageBaselineGuard>(), new PersistedResourceKeyReader(),
			new PageDataSourceReferenceValidator(new PageSchemaBodyParser()),
			Substitute.For<IPlatformVersionResolverFactory>(), Substitute.For<ISettingsRepository>());
	}

}
