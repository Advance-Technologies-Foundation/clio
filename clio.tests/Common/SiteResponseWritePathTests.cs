using System;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Command.McpServer.Tools;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using Clio.Common.EntitySchema;
using FluentAssertions;
using JsonReaderException = Newtonsoft.Json.JsonReaderException;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// The write paths a tool reports <c>site-response</c> from: each records the site's answer exactly when that
/// answer fails the call - a JSON refusal, an HTML page, an empty body with a failure status - and records
/// nothing on success or when the client did not keep the status.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class SiteResponseWritePathTests {

	private const string ServiceRefusal =
		"{\"success\":false,\"errorInfo\":{\"errorCode\":\"SchemaDesignerException\",\"message\":\"Column exists\",\"stackTrace\":\"at Secret()\"}}";

	private const string ErrorPage = "<html><head><title>500 - Internal server error.</title></head></html>";

	private SiteResponseCapture.Scope _capture;

	[SetUp]
	public void OpenCapture() => _capture = SiteResponseCapture.Begin();

	[TearDown]
	public void CloseCapture() => _capture.Dispose();

	// A client that keeps the status: every request is answered with the given answer.
	private static IApplicationClient KeepingClient(SiteHttpResponse answer) {
		IApplicationClient client = Substitute.For<IApplicationClient, IStatusPreservingApplicationClient>();
		((IStatusPreservingApplicationClient)client)
			.SendKeepingStatus(default, default, default, default, default, default, default)
			.ReturnsForAnyArgs(answer);
		return client;
	}

	#region Tests: entity schema designer (create/update/modify-entity-schema, create-lookup)

	[TestCase(200, ServiceRefusal, "Column exists", "json",
		TestName = "SchemaDesigner_ShouldRecordTheSiteAnswer_WhenTheServiceRefuses")]
	[TestCase(500, ErrorPage, null, "html",
		TestName = "SchemaDesigner_ShouldRecordTheSiteAnswer_WhenTheBodyIsAnHtmlPage")]
	[TestCase(502, "", null, "empty",
		TestName = "SchemaDesigner_ShouldRecordTheSiteAnswer_WhenTheBodyIsEmpty")]
	[Description("A designer request that fails records the site's answer before the failure leaves")]
	public void SchemaDesigner_ShouldRecordTheSiteAnswer(int status, string body, string message, string kind) {
		// Arrange
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(default(string)).ReturnsForAnyArgs("http://local/ServiceModel/EntitySchemaDesignerService.svc");
		RemoteEntitySchemaDesignerClient designer = new(KeepingClient(new SiteHttpResponse(status, body)),
			new Clio.Common.JsonConverter(), urls);

		// Act
		Action act = () => designer.SaveSchema(new EntityDesignSchemaDto(), new RemoteCommandOptions());

		// Assert
		act.Should().Throw<Exception>(because: "the save failed");
		_capture.Last.Should().BeEquivalentTo(new SiteResponse(status, message,
			message is null ? null : "SchemaDesignerException", kind));
	}

	[Test]
	[Description("A designer request that succeeds records nothing")]
	public void SchemaDesigner_ShouldRecordNothing_WhenTheServiceAccepts() {
		// Arrange
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(default(string)).ReturnsForAnyArgs("http://local/ServiceModel/EntitySchemaDesignerService.svc");
		RemoteEntitySchemaDesignerClient designer = new(KeepingClient(new SiteHttpResponse(200, "{\"success\":true}")),
			new Clio.Common.JsonConverter(), urls);

		// Act
		designer.SaveSchema(new EntityDesignSchemaDto(), new RemoteCommandOptions());

		// Assert
		_capture.Last.Should().BeNull();
	}

	[Test]
	[Description("A refused read the caller tolerates (a lookup probe) records nothing")]
	public void SchemaDesigner_ShouldRecordNothing_WhenAToleratedProbeIsRefused() {
		// Arrange
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(default(ServiceUrlBuilder.KnownRoute)).ReturnsForAnyArgs("http://local/0/DataService/json/SyncReply/SelectQuery");
		RemoteEntitySchemaDesignerClient designer = new(KeepingClient(new SiteHttpResponse(403,
			"{\"success\":false,\"errorInfo\":{\"message\":\"No rights to read Contact\"}}")), new Clio.Common.JsonConverter(), urls);

		// Act
		LookupRecordExistence existence = designer.CheckRecordExists("Contact", Guid.NewGuid(), new RemoteCommandOptions());

		// Assert
		existence.Should().Be(LookupRecordExistence.Unknown, because: "the probe's failure is tolerated");
		_capture.Last.Should().BeNull(
			because: "a tolerated refusal must not be reported as the site's answer to a later step that fails");
	}

	[Test]
	[Description("A refused catalog read records nothing even when it throws, because its callers may tolerate it")]
	public void SchemaDesigner_ShouldRecordNothing_WhenACatalogReadIsRefused() {
		// Arrange
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(default(string)).ReturnsForAnyArgs("http://local/ServiceModel/EntitySchemaDesignerService.svc");
		RemoteEntitySchemaDesignerClient designer = new(KeepingClient(new SiteHttpResponse(500, ServiceRefusal)),
			new Clio.Common.JsonConverter(), urls);

		// Act
		Action act = () => designer.GetSystemValues(Guid.NewGuid(), new RemoteCommandOptions());

		// Assert
		act.Should().Throw<InvalidOperationException>();
		_capture.Last.Should().BeNull();
	}

	#endregion

	#region Tests: DataService (data-binding-db writes)

	[Test]
	[Description("A DataService refusal is recorded with the site's own message")]
	public void DataService_ShouldRecordTheSiteAnswer_WhenTheEnvelopeRefuses() {
		// Act
		Action act = () => DataServiceResponse.ThrowIfUnsuccessful(new SiteHttpResponse(200,
			"{\"success\":false,\"errorInfo\":{\"message\":\"Duplicate key\"}}"), "InsertQuery");

		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("InsertQuery failed: Duplicate key*");
		_capture.Last.Should().BeEquivalentTo(new SiteResponse(200, "Duplicate key", null, SiteResponse.JsonBody));
	}

	[Test]
	[Description("An empty body with a failure status fails the write, where the body check alone accepts it")]
	public void DataService_ShouldFail_WhenTheBodyIsEmptyButTheStatusIsAFailure() {
		// Act
		Action act = () => DataServiceResponse.ThrowIfUnsuccessful(new SiteHttpResponse(500, ""), "InsertQuery");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("InsertQuery was not confirmed: the environment answered HTTP 500 without a service verdict, *");
		_capture.Last.Should().BeEquivalentTo(new SiteResponse(500, null, null, SiteResponse.EmptyBody));
	}

	[Test]
	[Description("An empty body with a 4xx is reported as a refusal, not as an unknown outcome")]
	public void DataService_ShouldReportARefusal_WhenTheBodyIsEmptyAndTheStatusIs4xx() {
		// Act
		Action act = () => DataServiceResponse.ThrowIfUnsuccessful(new SiteHttpResponse(403, ""), "InsertQuery");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("InsertQuery failed: the environment refused it with HTTP 403 and no service verdict.");
		_capture.Last.Status.Should().Be(403);
	}

	[Test]
	[Description("An HTML page is recorded with its status and no message")]
	public void DataService_ShouldRecordTheSiteAnswer_WhenTheBodyIsAnHtmlPage() {
		// Act
		Action act = () => DataServiceResponse.ThrowIfUnsuccessful(new SiteHttpResponse(403, ErrorPage), "DeleteQuery");

		// Assert
		act.Should().Throw<InvalidOperationException>();
		_capture.Last.Should().BeEquivalentTo(new SiteResponse(403, null, null, SiteResponse.HtmlBody));
	}

	[TestCase(200, "", TestName = "DataService_ShouldAccept_WhenA200HasAnEmptyBody")]
	[TestCase(null, "", TestName = "DataService_ShouldAccept_WhenTheStatusIsUnknownAndTheBodyIsEmpty")]
	[TestCase(204, "{\"success\":true}", TestName = "DataService_ShouldAccept_WhenTheEnvelopeSucceeds")]
	[Description("An accepted write records nothing, and an unknown status keeps today's body-only check")]
	public void DataService_ShouldAccept(int? status, string body) {
		// Act
		Action act = () => DataServiceResponse.ThrowIfUnsuccessful(new SiteHttpResponse(status, body), "InsertQuery");

		// Assert
		act.Should().NotThrow();
		_capture.Last.Should().BeNull();
	}

	#endregion

	#region Tests: OData writes (odata-update, odata-delete)

	[Test]
	[Description("An OData error is reported as before and recorded with the site's own message")]
	public void ODataWrite_ShouldReturnTheError_WhenTheSiteAnswersAnODataError() {
		// Arrange
		SiteHttpResponse answer = new(400, "{\"error\":{\"code\":\"\",\"message\":\"The property 'UsrNope' does not exist.\"}}");

		// Act
		string error = ODataKeyedWrite.ValidateWriteResponse(answer);

		// Assert
		error.Should().Contain("UsrNope");
		SiteResponse.FromRefusal(answer).Message.Should().Be("The property 'UsrNope' does not exist.");
	}

	[TestCase("", TestName = "ODataWrite_ShouldFail_WhenTheBodyIsEmptyButTheStatusIsAFailure")]
	[TestCase("{\"value\":1}", TestName = "ODataWrite_ShouldFail_WhenTheJsonIsUnknownButTheStatusIsAFailure")]
	[Description("A failure status fails the write even when the body alone would pass it")]
	public void ODataWrite_ShouldFailOnTheStatus(string body) {
		// Act
		string error = ODataKeyedWrite.ValidateWriteResponse(new SiteHttpResponse(503, body));

		// Assert
		error.Should().StartWith("Creatio answered the write with HTTP 503",
			because: "an empty or unrecognized body used to read as a successful write");
		error.Should().Contain("may or may not have been applied",
			because: "a gateway can answer after the write was applied, so the outcome is unknown, not refused");
	}

	[TestCase(204, "", TestName = "ODataWrite_ShouldAccept_WhenA204HasNoBody")]
	[TestCase(null, "", TestName = "ODataWrite_ShouldAccept_WhenTheStatusIsUnknownAndTheBodyIsEmpty")]
	[Description("A 2xx answer, or one with an unknown status, keeps today's body-only check")]
	public void ODataWrite_ShouldAccept(int? status, string body) {
		ODataKeyedWrite.ValidateWriteResponse(new SiteHttpResponse(status, body)).Should().BeNull();
	}

	[TestCase(404, TestName = "ODataWrite_ShouldReportARefusal_WhenA404HasNoBody")]
	[TestCase(302, TestName = "ODataWrite_ShouldReportARefusal_WhenA302ReachesTheCaller")]
	[Description("A 3xx or 4xx with no error body is a refusal: the write was not applied")]
	public void ODataWrite_ShouldReportARefusal(int status) {
		// Act
		string error = ODataKeyedWrite.ValidateWriteResponse(new SiteHttpResponse(status, ""));

		// Assert
		error.Should().Be($"Creatio refused the write with HTTP {status} and no recognized error body; it was not applied.");
	}

	[Test]
	[Description("A refused DELETE carries the site's answer on its outcome")]
	public void ODataDelete_ShouldCarryTheSiteAnswer_WhenTheDeleteIsRefused() {
		// Arrange
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(default(string)).ReturnsForAnyArgs("http://local/0/odata/Contact(1)");
		RecordDeletionService service = new(KeepingClient(new SiteHttpResponse(403,
				"{\"error\":{\"code\":\"\",\"message\":\"No rights to delete\"}}")), urls,
			Substitute.For<IRuntimeEntitySchemaReader>());

		// Act
		RecordDeleteOutcome outcome = service.Delete("Contact", Guid.NewGuid().ToString(), 1_000);

		// Assert
		outcome.Success.Should().BeFalse();
		outcome.SiteResponse.Should().BeEquivalentTo(new SiteResponse(403, "No rights to delete", null,
			SiteResponse.JsonBody));
	}

	[Test]
	[Description("A DELETE with no HTTP answer carries no site response")]
	public void ODataDelete_ShouldCarryNoSiteAnswer_WhenThereIsNoResponse() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient, IStatusPreservingApplicationClient>();
		((IStatusPreservingApplicationClient)client)
			.SendKeepingStatus(default, default, default, default, default, default, default)
			.ReturnsForAnyArgs(_ => throw new AggregateException(new System.Net.Http.HttpRequestException("refused")));
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(default(string)).ReturnsForAnyArgs("http://local/0/odata/Contact(1)");
		RecordDeletionService service = new(client, urls, Substitute.For<IRuntimeEntitySchemaReader>());

		// Act
		RecordDeleteOutcome outcome = service.Delete("Contact", Guid.NewGuid().ToString(), 1_000);

		// Assert
		outcome.Success.Should().BeFalse();
		outcome.SiteResponse.Should().BeNull(because: "no HTTP response exists to report");
	}

	#endregion

	#region Tests: page designer (create-page, update-page, sync-pages)

	[TestCase(500, ErrorPage, "html", TestName = "PageDesigner_ShouldRecordTheSiteAnswer_WhenTheBodyIsAnHtmlPage")]
	[TestCase(502, "", "empty", TestName = "PageDesigner_ShouldRecordTheSiteAnswer_WhenTheBodyIsEmpty")]
	[Description("A designer answer that is not JSON is recorded before the parser's exception leaves")]
	public void PageDesigner_ShouldRecordTheSiteAnswer_WhenTheBodyIsNotJson(int status, string body, string kind) {
		// Act
		Action act = () => PageSchemaMetadataHelper.PostDesignerRequest(
			KeepingClient(new SiteHttpResponse(status, body)), "http://local/x", "{}");

		// Assert
		act.Should().Throw<JsonReaderException>(because: "the caller reports the parse failure as before");
		_capture.Last.Should().BeEquivalentTo(new SiteResponse(status, null, null, kind));
	}

	[Test]
	[Description("An advisory read does not record a non-JSON answer")]
	public void PageDesigner_ShouldRecordNothing_WhenTheReadIsAdvisory() {
		// Act
		Action act = () => PageSchemaMetadataHelper.PostDesignerRequest(
			KeepingClient(new SiteHttpResponse(500, ErrorPage)), "http://local/x", "{}",
			recordRefusal: false);

		// Assert
		act.Should().Throw<JsonReaderException>();
		_capture.Last.Should().BeNull(because: "a tolerated read must not be reported as the cause of a later failure");
	}

	[Test]
	[Description("A JSON answer is returned with the status kept for the caller to record a refusal")]
	public void PageDesigner_ShouldReturnTheAnswer_WhenTheBodyIsJson() {
		// Act
		var (parsed, answer) = PageSchemaMetadataHelper.PostDesignerRequest(
			KeepingClient(new SiteHttpResponse(200, ServiceRefusal)), "http://local/x", "{}");

		// Assert
		parsed["success"]!.ToString().Should().Be("False");
		answer.StatusCode.Should().Be(200);
		_capture.Last.Should().BeNull(because: "whether a JSON answer is a refusal is the caller's decision");
	}

	#endregion

	#region Tests: restart-by-environment-name

	[TestCase(403, TestName = "Restart_ShouldFail_WhenTheSiteAnswers403")]
	[TestCase(404, TestName = "Restart_ShouldFail_WhenTheSiteAnswers404")]
	[Description("A restart the site refuses with a 4xx status fails the call and records the site's answer")]
	public void Restart_ShouldFail_WhenTheSiteRefuses(int status) {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		RestartCommand restart = new(KeepingClient(new SiteHttpResponse(status, ErrorPage)),
			new EnvironmentSettings { Uri = "http://test.domain.com", IsNetCore = true },
			Substitute.For<IServerReadinessWaiter>()) { Logger = logger };

		// Act
		int exitCode = restart.Execute(new RestartOptions());

		// Assert
		exitCode.Should().Be(1, because: "a refused restart used to report 'Done' with exit code 0");
		_capture.Last.Should().BeEquivalentTo(new SiteResponse(status, null, null, SiteResponse.HtmlBody));
		logger.Received(1).WriteError(Arg.Is<string>(text => text.Contains($"HTTP {status}")));
	}

	[TestCase(500, TestName = "Restart_ShouldWarn_WhenTheSiteAnswers500")]
	[TestCase(502, TestName = "Restart_ShouldWarn_WhenTheSiteAnswers502")]
	[TestCase(503, TestName = "Restart_ShouldWarn_WhenTheSiteAnswers503")]
	[Description("A 5xx answer, which a restart in progress behind a proxy can produce, is a warning and not a refusal")]
	public void Restart_ShouldWarnAndGoOn_WhenTheSiteAnswers5xx(int status) {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		RestartCommand restart = new(KeepingClient(new SiteHttpResponse(status, ErrorPage)),
			new EnvironmentSettings { Uri = "http://test.domain.com", IsNetCore = true },
			Substitute.For<IServerReadinessWaiter>()) { Logger = logger };

		// Act
		int exitCode = restart.Execute(new RestartOptions());

		// Assert
		exitCode.Should().Be(0, because: "the restart may have happened; without the readiness wait nothing more is known");
		_capture.Last.Should().BeNull(because: "only a refusal carries a site response");
		logger.Received(1).WriteWarning(Arg.Is<string>(text => text.Contains($"HTTP {status}")
			&& text.Contains("not confirmed")));
		logger.DidNotReceive().WriteError(Arg.Any<string>());
	}

	[TestCase(200, TestName = "Restart_ShouldSucceed_WhenTheSiteAnswers200")]
	[TestCase(204, TestName = "Restart_ShouldSucceed_WhenTheSiteAnswers204")]
	[Description("A 2xx answer is a restart the site accepted; its body is not read")]
	public void Restart_ShouldSucceed_WhenTheSiteAccepts(int status) {
		// Arrange
		RestartCommand restart = new(KeepingClient(new SiteHttpResponse(status, "anything")),
			new EnvironmentSettings { Uri = "http://test.domain.com", IsNetCore = true },
			Substitute.For<IServerReadinessWaiter>()) { Logger = Substitute.For<ILogger>() };

		// Act
		int exitCode = restart.Execute(new RestartOptions());

		// Assert
		exitCode.Should().Be(0);
		_capture.Last.Should().BeNull();
	}

	[Test]
	[Description("A client that does not keep the status restarts as before: the answer is not judged")]
	public void Restart_ShouldSucceed_WhenTheClientDoesNotKeepTheStatus() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(default, default, default, default, default).ReturnsForAnyArgs(ErrorPage);
		RestartCommand restart = new(client,
			new EnvironmentSettings { Uri = "http://test.domain.com", IsNetCore = true },
			Substitute.For<IServerReadinessWaiter>()) { Logger = Substitute.For<ILogger>() };

		// Act
		int exitCode = restart.Execute(new RestartOptions());

		// Assert
		exitCode.Should().Be(0, because: "without a status there is nothing honest to judge the answer by");
	}

	#endregion

}
