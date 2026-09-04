namespace Clio.Tests.Command;

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

/// <summary>
/// Covers TC-U-F01 and TC-U-F02 of the mcp-worker-execution-boundary test plan: a transport or auth failure
/// inside <c>PageSchemaMetadataHelper.ExecuteSelectQuery</c> must never surface as an answer about the
/// requested data. Every case drives <c>QueryPackageUId</c>, the producer of the
/// <c>"Failed to query SysPackage"</c> message named in story 11.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class PageSchemaMetadataHelperTransportErrorTests
{
	private const string TestBase = "http://test";
	private const string SelectQueryUrl = TestBase + "/DataService/json/SyncReply/SelectQuery";
	private const string SelectQueryPath = "/DataService/json/SyncReply/SelectQuery";
	private const string PackageName = "UsrCustomPackage";
	private const string DomainFailureMessage = "Failed to query SysPackage";

	private const string LoginPageBody =
		"<!DOCTYPE html><html><head><title>Creatio</title></head><body><form id=\"loginForm\">"
		+ "<input name=\"UserName\" /></form></body></html>";

	private IApplicationClient _applicationClient;
	private IServiceUrlBuilder _serviceUrlBuilder;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilder.Build(SelectQueryPath).Returns(SelectQueryUrl);
	}

	[TearDown]
	public void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_serviceUrlBuilder.ClearReceivedCalls();
	}

	[Test]
	[Description("TC-U-F01: an HTML login page must produce an auth/transport error naming the HTML page and the endpoint, never the domain 'Failed to query SysPackage' answer.")]
	public void QueryPackageUId_ShouldReportHtmlLoginPage_WhenSessionExpired() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(LoginPageBody);

		// Act
		(string uId, string error) = PageSchemaMetadataHelper.QueryPackageUId(
			_applicationClient, _serviceUrlBuilder, PackageName);

		// Assert
		uId.Should().BeNull(because: "a login page carries no package identifier");
		error.Should().NotBe(DomainFailureMessage,
			because: "an expired session is not a statement about the SysPackage data, and reporting it as one sends the caller to debug their package");
		error.Should().Contain("HTML page",
			because: "the caller must be told the body was an HTML page so they check their session rather than their data");
		error.Should().Contain(SelectQueryUrl,
			because: "naming the endpoint is what makes the error actionable across the several lookups this helper performs");
		error.Should().NotContain("loginForm",
			because: "a login page can carry session tokens, so the guard deliberately omits the body preview for markup");
	}

	[Test]
	[Description("TC-U-F01: an empty response body must produce the shared empty-body transport message rather than the domain failure answer.")]
	public void QueryPackageUId_ShouldReportEmptyBody_WhenServerSendsNoBody() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(string.Empty);

		// Act
		(string uId, string error) = PageSchemaMetadataHelper.QueryPackageUId(
			_applicationClient, _serviceUrlBuilder, PackageName);

		// Assert
		uId.Should().BeNull(because: "an empty body carries no package identifier");
		error.Should().NotBe(DomainFailureMessage,
			because: "a body-less answer says nothing about SysPackage and must not be reported as a data failure");
		error.Should().Contain("empty response",
			because: "the message must name the actual cause so the caller retries instead of inspecting their package");
		error.Should().Contain(SelectQueryUrl, because: "the endpoint identifies which request produced no body");
	}

	[Test]
	[Description("TC-U-F01: an HTTP 500 must produce a transport error carrying the status detail, distinct from a timeout and from the domain failure answer.")]
	public void QueryPackageUId_ShouldReportTransportFailure_WhenServerReturnsInternalServerError() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Throws(new HttpRequestException("Response status code does not indicate success: 500 (Internal Server Error)."));

		// Act
		(string uId, string error) = PageSchemaMetadataHelper.QueryPackageUId(
			_applicationClient, _serviceUrlBuilder, PackageName);

		// Assert
		uId.Should().BeNull(because: "a rejected request carries no package identifier");
		error.Should().NotBe(DomainFailureMessage,
			because: "a server-side 500 is not a statement about the SysPackage data");
		error.Should().Contain("Transport error",
			because: "the caller must be able to tell a transport failure apart from a rejected query");
		error.Should().Contain("500",
			because: "carrying the HTTP status through is what makes the 500 case actionable and distinguishable");
		error.Should().NotContain("timed out",
			because: "a rejected request and a request that never answered need different remedies");
	}

	[Test]
	[Description("TC-U-F01: a client-side timeout surfaced as TaskCanceledException must produce a timeout error, distinct from a generic transport failure.")]
	public void QueryPackageUId_ShouldReportTimeout_WhenRequestIsCancelledByTheClient() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Throws(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing."));

		// Act
		(string uId, string error) = PageSchemaMetadataHelper.QueryPackageUId(
			_applicationClient, _serviceUrlBuilder, PackageName);

		// Assert
		uId.Should().BeNull(because: "a timed-out request carries no package identifier");
		error.Should().NotBe(DomainFailureMessage,
			because: "a timeout says nothing about the SysPackage data and must not be reported as a data failure");
		error.Should().Contain("timed out",
			because: "the caller must be told the environment never answered so they retry instead of debugging their package");
		error.Should().Contain(SelectQueryUrl, because: "the endpoint identifies which request timed out");
	}

	[Test]
	[Description("TC-U-F01: a WebRequest-shaped read timeout (WebException with Timeout status) must be classified as a timeout, not as a generic transport failure.")]
	public void QueryPackageUId_ShouldReportTimeout_WhenWebExceptionStatusIsTimeout() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Throws(new WebException("The operation has timed out.", WebExceptionStatus.Timeout));

		// Act
		(string uId, string error) = PageSchemaMetadataHelper.QueryPackageUId(
			_applicationClient, _serviceUrlBuilder, PackageName);

		// Assert
		uId.Should().BeNull(because: "a timed-out request carries no package identifier");
		error.Should().Contain("timed out",
			because: "the two client shapes in use report a read timeout differently, and both must reach the timeout message");
		error.Should().NotContain("Transport error",
			because: "a timeout must stay distinguishable from a refused or failed connection");
	}

	[Test]
	[Description("TC-U-F01: the HTML login page, the HTTP 500 and the timeout must produce three pairwise distinct errors, none of them the domain failure answer.")]
	public void QueryPackageUId_ShouldProduceDistinctErrors_ForLoginPageAndServerErrorAndTimeout() {
		// Arrange
		string loginPageError = QueryPackageUIdWithResponse(LoginPageBody);
		string serverError = QueryPackageUIdWithFailure(
			new HttpRequestException("Response status code does not indicate success: 500 (Internal Server Error)."));
		string timeoutError = QueryPackageUIdWithFailure(new TaskCanceledException("timeout"));

		// Act
		string[] errors = [loginPageError, serverError, timeoutError];

		// Assert
		errors.Should().OnlyHaveUniqueItems(
			because: "AC-02 requires each failure class to be separately actionable, which one shared message cannot be");
		errors.Should().NotContain(DomainFailureMessage,
			because: "AC-01 forbids any of these three failure classes from surfacing as the domain answer");
	}

	[Test]
	[Description("TC-U-F02: an unexpected exception must propagate instead of being converted into a domain answer by a catch-all.")]
	public void QueryPackageUId_ShouldPropagate_WhenExceptionIsNotATransportFailure() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Throws(new InvalidCastException("unexpected programming error"));

		// Act
		Action act = () => PageSchemaMetadataHelper.QueryPackageUId(
			_applicationClient, _serviceUrlBuilder, PackageName);

		// Assert
		act.Should().Throw<InvalidCastException>(
			because: "the bare catch is gone: an unexpected failure must reach the caller's own handler rather than be reported as a SysPackage lookup result");
	}

	[Test]
	[Description("AC-04: a genuine empty result must stay an empty result — the not-found answer, not a transport error.")]
	public void QueryPackageUId_ShouldReportNotFound_WhenServerAnswersWithZeroRows() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{"success": true, "rows": []}""");

		// Act
		(string uId, string error) = PageSchemaMetadataHelper.QueryPackageUId(
			_applicationClient, _serviceUrlBuilder, PackageName);

		// Assert
		uId.Should().BeNull(because: "no SysPackage row matched the requested name");
		error.Should().Contain(PackageName,
			because: "an answered query with no rows is a not-found answer about the named package");
		error.Should().NotContain("Transport error",
			because: "the environment answered, so nothing about the transport failed");
		error.Should().NotContain("timed out", because: "the environment answered within the timeout");
	}

	[Test]
	[Description("AC-04 boundary: a DataService rejection (success=false) must keep producing the stable domain message, not a transport error.")]
	public void QueryPackageUId_ShouldKeepDomainMessage_WhenDataServiceRejectsTheQuery() {
		// Arrange
		_applicationClient.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>())
			.Returns("""{"success": false}""");

		// Act
		(string uId, string error) = PageSchemaMetadataHelper.QueryPackageUId(
			_applicationClient, _serviceUrlBuilder, PackageName);

		// Assert
		uId.Should().BeNull(because: "a rejected query cannot yield a package identifier");
		error.Should().Be(DomainFailureMessage,
			because: "the service answered and rejected the query, which is the one case the domain message legitimately describes");
	}

	/// <summary>Runs the lookup against a substitute that answers with the given body.</summary>
	private string QueryPackageUIdWithResponse(string responseBody) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Returns(responseBody);
		(_, string error) = PageSchemaMetadataHelper.QueryPackageUId(client, _serviceUrlBuilder, PackageName);
		return error;
	}

	/// <summary>Runs the lookup against a substitute that fails the request with the given exception.</summary>
	private string QueryPackageUIdWithFailure(Exception failure) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecutePostRequest(SelectQueryUrl, Arg.Any<string>()).Throws(failure);
		(_, string error) = PageSchemaMetadataHelper.QueryPackageUId(client, _serviceUrlBuilder, PackageName);
		return error;
	}
}
