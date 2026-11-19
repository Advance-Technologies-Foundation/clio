using System.Text.Json;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class SiteResponseTests {

	#region Tests: FromRefusal

	[Test]
	[Description("A Creatio service envelope refusal carries the status, errorInfo.message and errorInfo.errorCode, and never the stack trace")]
	public void FromRefusal_ShouldTakeMessageAndCodeFromErrorInfo_WhenTheBodyIsAServiceEnvelope() {
		// Arrange
		SiteHttpResponse answer = new(200,
			"{\"success\":false,\"errorInfo\":{\"errorCode\":\"SchemaDesignerException\","
			+ "\"message\":\"Column 'UsrFoo' already exists\",\"stackTrace\":\"at Terrasoft.Secret.Frame()\"}}");

		// Act
		SiteResponse response = SiteResponse.FromRefusal(answer);

		// Assert
		response.Should().BeEquivalentTo(new SiteResponse(200, "Column 'UsrFoo' already exists",
			"SchemaDesignerException", SiteResponse.JsonBody),
			because: "the status is what the site sent (200 with success:false) and the message and code are its own");
		JsonSerializer.Serialize(response).Should().NotContain("Terrasoft.Secret.Frame",
			because: "errorInfo.stackTrace is never copied into the site response");
	}

	[Test]
	[Description("An OData v4 error carries error.message and error.code")]
	public void FromRefusal_ShouldReadODataV4Error_WhenMessageIsAString() {
		// Arrange
		SiteHttpResponse answer = new(400,
			"{\"error\":{\"code\":\"\",\"message\":\"The property 'UsrNope' does not exist.\",\"innererror\":{\"message\":\"inner\",\"stacktrace\":\"at X\"}}}");

		// Act
		SiteResponse response = SiteResponse.FromRefusal(answer);

		// Assert
		response.Status.Should().Be(400, because: "the HTTP status is carried as sent");
		response.Message.Should().Be("The property 'UsrNope' does not exist.",
			because: "the OData headline is the site's own message");
		response.Code.Should().BeNull(because: "a blank code is no code");
		response.Body.Should().Be(SiteResponse.JsonBody);
	}

	[Test]
	[Description("An OData v3 error carries error.message.value")]
	public void FromRefusal_ShouldReadODataV3Error_WhenMessageIsAnObject() {
		// Arrange
		SiteHttpResponse answer = new(500,
			"{\"error\":{\"code\":\"E1\",\"message\":{\"lang\":\"en-US\",\"value\":\"An error has occurred.\"}}}");

		// Act
		SiteResponse response = SiteResponse.FromRefusal(answer);

		// Assert
		response.Message.Should().Be("An error has occurred.", because: "v3 nests the text under message.value");
		response.Code.Should().Be("E1", because: "error.code is the site's own code");
	}

	[Test]
	[Description("A DataService responseStatus refusal carries responseStatus.Message and ErrorCode")]
	public void FromRefusal_ShouldReadResponseStatus_WhenTheBodyIsADataServiceAnswer() {
		// Arrange
		SiteHttpResponse answer = new(200,
			"{\"success\":false,\"responseStatus\":{\"ErrorCode\":\"SecurityException\",\"Message\":\"No rights to insert\"}}");

		// Act
		SiteResponse response = SiteResponse.FromRefusal(answer);

		// Assert
		response.Message.Should().Be("No rights to insert");
		response.Code.Should().Be("SecurityException");
	}

	[Test]
	[Description("A WCF wrapped answer ({\"<Method>Result\": {...}}) is read inside the wrapper")]
	public void FromRefusal_ShouldReadInsideTheWrapper_WhenTheBodyIsAWrappedWcfAnswer() {
		// Arrange
		SiteHttpResponse answer = new(200,
			"{\"BuildProcessResult\":{\"success\":false,\"errorMessage\":\"Element 'Start1' has no outgoing flow\"}}");

		// Act
		SiteResponse response = SiteResponse.FromRefusal(answer);

		// Assert
		response.Message.Should().Be("Element 'Start1' has no outgoing flow",
			because: "BuildProcess answers in a WCF wrapper and its message is the site's own");
	}

	[Test]
	[Description("An HTML error page carries the status and body kind, and no message")]
	public void FromRefusal_ShouldCarryNoMessage_WhenTheBodyIsAnHtmlPage() {
		// Arrange
		SiteHttpResponse answer = new(502,
			"  <!DOCTYPE html><html><head><title>502 - Bad Gateway</title></head><body>proxy</body></html>");

		// Act
		SiteResponse response = SiteResponse.FromRefusal(answer);

		// Assert
		response.Should().BeEquivalentTo(new SiteResponse(502, null, null, SiteResponse.HtmlBody),
			because: "a page is not quoted: only its status and that it was a page are reported");
	}

	[TestCase("", TestName = "FromRefusal_ShouldReportEmpty_WhenTheBodyIsEmpty")]
	[TestCase("  \r\n ", TestName = "FromRefusal_ShouldReportEmpty_WhenTheBodyIsWhitespace")]
	[TestCase(null, TestName = "FromRefusal_ShouldReportEmpty_WhenTheBodyIsNull")]
	[Description("An empty body carries the status and the empty body kind")]
	public void FromRefusal_ShouldReportEmpty(string body) {
		// Act
		SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(503, body));

		// Assert
		response.Should().BeEquivalentTo(new SiteResponse(503, null, null, SiteResponse.EmptyBody));
	}

	[TestCase("Service Unavailable", TestName = "FromRefusal_ShouldReportText_WhenTheBodyIsPlainText")]
	[TestCase("{\"success\":false,", TestName = "FromRefusal_ShouldReportText_WhenTheBodyIsBrokenJson")]
	[Description("A body that is neither markup nor parseable JSON is text, with no message")]
	public void FromRefusal_ShouldReportText(string body) {
		// Act
		SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(500, body));

		// Assert
		response.Should().BeEquivalentTo(new SiteResponse(500, null, null, SiteResponse.TextBody),
			because: "only a known JSON shape yields a message; free text is not quoted");
	}

	[Test]
	[Description("JSON of an unknown shape carries no message")]
	public void FromRefusal_ShouldCarryNoMessage_WhenTheJsonShapeIsUnknown() {
		// Act
		SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(500, "{\"value\":[1,2]}"));

		// Assert
		response.Should().BeEquivalentTo(new SiteResponse(500, null, null, SiteResponse.JsonBody));
	}

	[Test]
	[Description("An answer whose status the client did not keep yields no site response")]
	public void FromRefusal_ShouldReturnNull_WhenTheStatusIsUnknown() {
		// Act
		SiteResponse response = SiteResponse.FromRefusal(
			new SiteHttpResponse(null, "{\"success\":false,\"errorInfo\":{\"message\":\"x\"}}"));

		// Assert
		response.Should().BeNull(because: "a site response without the real status would not be honest");
	}

	[Test]
	[Description("A message longer than the cap is cut to the cap and ends with an ellipsis")]
	public void FromRefusal_ShouldCapTheMessage_WhenItIsLongerThanTheCap() {
		// Arrange
		string longMessage = new('a', SiteResponse.MaxMessageLength + 50);
		SiteHttpResponse answer = new(200, JsonSerializer.Serialize(new {
			success = false, errorInfo = new { message = longMessage }
		}));

		// Act
		SiteResponse response = SiteResponse.FromRefusal(answer);

		// Assert
		response.Message.Should().HaveLength(SiteResponse.MaxMessageLength);
		response.Message.Should().EndWith("…");
	}

	[Test]
	[Description("The cap never splits a surrogate pair")]
	public void FromRefusal_ShouldNotSplitASurrogatePair_WhenTheCapFallsInsideOne() {
		// Arrange - an emoji (two UTF-16 units) straddles the cut point.
		string prefix = new('a', SiteResponse.MaxMessageLength - 2);
		string longMessage = prefix + "\U0001F600" + new string('b', 20);
		SiteHttpResponse answer = new(200, JsonSerializer.Serialize(new {
			success = false, errorInfo = new { message = longMessage }
		}));

		// Act
		SiteResponse response = SiteResponse.FromRefusal(answer);

		// Assert
		response.Message.Should().Be(prefix + "…",
			because: "a lone high surrogate is not valid text, so the cut moves before the pair");
	}

	#endregion

	[Test]
	[Description("The site's message and code pass the same sensitive-text redaction as the error text beside them")]
	public void FromRefusal_ShouldRedactSensitiveText_InTheMessageAndCode() {
		// Arrange
		SiteHttpResponse answer = new(500, JsonSerializer.Serialize(new {
			success = false,
			errorInfo = new {
				errorCode = "Npgsql password=Secret1",
				message = "Cannot connect to db-host.internal:5432 for https://tenant.example.com/0/odata (Password=Secret1)"
			}
		}));

		// Act
		SiteResponse response = SiteResponse.FromRefusal(answer);

		// Assert
		response.Message.Should().NotContain("Secret1").And.NotContain("db-host.internal:5432")
			.And.NotContain("https://tenant.example.com", because: "a result reaches a model transcript");
		response.Message.Should().StartWith("Cannot connect to", because: "the rest of the site's words are kept");
		response.Code.Should().NotContain("Secret1");
	}

	#region Tests: Serialization

	[Test]
	[Description("The site response serializes with lower-case members and leaves out a missing message and code")]
	public void Serialize_ShouldUseTheAgreedMemberNames_AndOmitMissingMembers() {
		// Act
		string json = JsonSerializer.Serialize(new SiteResponse(502, null, null, SiteResponse.HtmlBody));
		string newtonsoft = Newtonsoft.Json.JsonConvert.SerializeObject(new SiteResponse(502, null, null,
			SiteResponse.HtmlBody));

		// Assert
		json.Should().Be("{\"status\":502,\"body\":\"html\"}",
			because: "callers read status, message, code and body, and absent members are left out");
		newtonsoft.Should().Be(json, because: "the CLI's Newtonsoft path writes the same shape");
	}

	#endregion

	#region Tests: refusal without processing

	[TestCase(405)]
	[TestCase(413)]
	[TestCase(414)]
	[TestCase(415)]
	[Description("The statuses with which HTTP defines the server as refusing to process the request")]
	public void IsRefusedWithoutProcessing_ShouldBeTrue_ForAStatusThatRefusesToProcess(int status) {
		new SiteHttpResponse(status, "").IsRefusedWithoutProcessing.Should().BeTrue();
		new SiteHttpResponse(status, "\uFEFF  ").IsRefusedWithoutProcessing.Should().BeTrue();
		new SiteHttpResponse(status, "<html>refused</html>").IsRefusedWithoutProcessing.Should().BeTrue();
		new SiteHttpResponse(status, "  <!DOCTYPE html><html><title>403</title></html>").IsRefusedWithoutProcessing
			.Should().BeTrue();
		new SiteHttpResponse(status, "<HTML><BODY>Forbidden</BODY></HTML>").IsRefusedWithoutProcessing.Should().BeTrue();
		new SiteHttpResponse(status, "403 Forbidden").IsRefusedWithoutProcessing.Should().BeTrue(
			because: "text that starts like a number but is not JSON is plain text");
		new SiteHttpResponse(status, "null pointer").IsRefusedWithoutProcessing.Should().BeTrue();
		new SiteHttpResponse(status, "Request body too large").IsRefusedWithoutProcessing.Should().BeTrue();
		SiteHttpResponse.HasRefusalStatus(status).Should().BeTrue();
	}

	[TestCase("{\"error\":{\"code\":\"\",\"message\":\"No rights\"}}", TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA413CarriesAnODataError")]
	[TestCase("{\"success\":false,\"errorInfo\":{\"message\":\"No rights\"}}", TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA413CarriesAServiceEnvelope")]
	[TestCase("{\"success\":false,", TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA413CarriesBrokenJson")]
	[TestCase("  [1]", TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA413CarriesAJsonArray")]
	[TestCase("\uFEFF{\"success\":false,\"errorInfo\":{\"message\":\"No rights\"}}",
		TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA413CarriesJsonAfterAByteOrderMark")]
	[TestCase("\"No rights\"", TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA413CarriesAJsonString")]
	[TestCase("false", TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA413CarriesAJsonScalar")]
	[TestCase("<?xml version=\"1.0\"?><Fault><Reason>No rights</Reason></Fault>",
		TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA413CarriesAnXmlFault")]
	[TestCase("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body/></s:Envelope>",
		TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA413CarriesASoapEnvelope")]
	[Description("A body that starts like JSON may be the application's own answer, written after part of its work")]
	public void IsRefusedWithoutProcessing_ShouldBeFalse_WhenTheBodyMayBeTheApplications(string body) {
		new SiteHttpResponse(413, body).IsRefusedWithoutProcessing.Should().BeFalse();
		SiteResponse.FromRefusal(new SiteHttpResponse(413, body)).RefusedWithoutProcessing.Should().BeFalse();
	}

	[TestCase("", TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenAnEmpty403Answers")]
	[TestCase("<html><title>403 - Forbidden</title></html>", TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA403PageAnswers")]
	[TestCase("Forbidden", TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA403TextAnswers")]
	[TestCase("{\"Message\":\"Authentication failed.\"}", TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA403CarriesTheAuthenticationEnvelope")]
	[Description("No 403 proves anything: a gateway that inspects responses answers 403 after the backend ran (#ENG-92711)")]
	public void IsRefusedWithoutProcessing_ShouldBeFalse_ForAny403(string body) {
		new SiteHttpResponse(403, body).IsRefusedWithoutProcessing.Should().BeFalse();
		SiteHttpResponse.HasRefusalStatus(403).Should().BeFalse();
	}

	[TestCase("", TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenAnEmpty401Answers")]
	[TestCase("<html><title>401 - Unauthorized</title></html>", TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenA401PageAnswers")]
	[TestCase("{\"Message\":\"Authentication failed.\",\"StackTrace\":null,\"ExceptionType\":\"System.InvalidOperationException\"}",
		TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenTheAuthenticationEnvelopeAnswers")]
	[Description("No 401 proves anything: creatio.client signs in again and re-sends after any 401, so the 401 a caller sees answers the second send, and ASP.NET writes its 401 envelope for any redirect during a REST call, one after the handler ran included")]
	public void IsRefusedWithoutProcessing_ShouldBeFalse_ForAny401(string body) {
		// Act
		SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(401, body));

		// Assert
		new SiteHttpResponse(401, body).IsRefusedWithoutProcessing.Should().BeFalse();
		response.RefusedWithoutProcessing.Should().BeFalse();
		SiteHttpResponse.HasRefusalStatus(401).Should().BeFalse();
	}

	[Test]
	[Description("431 proves nothing: a re-send after signing in again carries other cookies and headers than the first send")]
	public void IsRefusedWithoutProcessing_ShouldBeFalse_For431() {
		new SiteHttpResponse(431, "").IsRefusedWithoutProcessing.Should().BeFalse();
		SiteHttpResponse.HasRefusalStatus(431).Should().BeFalse();
	}

	[Test]
	[Description("An answer to a request the client sent after following a redirect proves nothing about the write")]
	public void IsRefusedWithoutProcessing_ShouldBeFalse_WhenTheAnswerFollowedARedirect() {
		new SiteHttpResponse(405, "") { Redirected = true }.IsRefusedWithoutProcessing.Should().BeFalse();
	}

	[TestCase("POST", "http://site/0/ServiceModel/X.svc/Save", false, TestName = "Read_ShouldNotMarkARedirect_WhenTheAnswerIsToTheRequestSent")]
	[TestCase("GET", "http://site/0/ServiceModel/X.svc/Save", true, TestName = "Read_ShouldMarkARedirect_WhenTheClientTurnedThePostIntoAGet")]
	[TestCase("POST", "http://site/0/Shell/", true, TestName = "Read_ShouldMarkARedirect_WhenTheAnswerIsToAnotherAddress")]
	[TestCase("post", "HTTP://SITE/0/ServiceModel/X.svc/Save", false, TestName = "Read_ShouldNotMarkARedirect_WhenOnlyTheCaseDiffers")]
	[Description("Read compares the request the response belongs to with the one clio sent")]
	public void Read_ShouldMarkARedirect_OnlyWhenTheMethodOrAddressChanged(string answeredMethod, string answeredUrl,
		bool redirected) {
		// Arrange
		using System.Net.Http.HttpResponseMessage response = new(System.Net.HttpStatusCode.MethodNotAllowed) {
			RequestMessage = new System.Net.Http.HttpRequestMessage(new System.Net.Http.HttpMethod(answeredMethod),
				answeredUrl)
		};

		// Act
		SiteHttpResponse answer = SiteHttpResponse.Read(response, "", System.Net.Http.HttpMethod.Post,
			"http://site/0/ServiceModel/X.svc/Save");

		// Assert
		answer.StatusCode.Should().Be(405);
		answer.Redirected.Should().Be(redirected);
		answer.IsRefusedWithoutProcessing.Should().Be(!redirected);
	}

	[Test]
	[Description("The authentication envelope's own words still reach the site response, to be quoted as not confirmed")]
	public void FromRefusal_ShouldCarryTheAuthenticationEnvelopesMessage() {
		// Act
		SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(401,
			"{\"Message\":\"Authentication failed.\",\"StackTrace\":null,\"ExceptionType\":\"System.InvalidOperationException\"}"));

		// Assert
		response.Message.Should().Be("Authentication failed.");
		response.Body.Should().Be(SiteResponse.JsonBody);
	}

	[Test]
	[Description("JSON after a byte-order mark is still read as JSON, with the site's own message")]
	public void FromRefusal_ShouldReadJson_AfterAByteOrderMark() {
		// Act
		SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(500,
			"\uFEFF{\"success\":false,\"errorInfo\":{\"message\":\"Schema is locked\"}}"));

		// Assert
		response.Should().BeEquivalentTo(new SiteResponse(500, "Schema is locked", null, SiteResponse.JsonBody),
			because: "some Creatio WCF endpoints start their JSON with a byte-order mark");
	}

	[Test]
	[Description("An answer that followed clio's own re-send never proves a refusal without processing")]
	public void IsRefusedWithoutProcessing_ShouldBeFalse_WhenTheRequestWasResent() {
		new SiteHttpResponse(413, "") { Resent = true }.IsRefusedWithoutProcessing.Should().BeFalse();
	}

	[Test]
	[Description("The site response remembers the proof without serializing it")]
	public void FromRefusal_ShouldCarryTheProof_WithoutSerializingIt() {
		// Act
		SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(413, "<html>Request Entity Too Large</html>"));

		// Assert
		response.RefusedWithoutProcessing.Should().BeTrue();
		JsonSerializer.Serialize(response).Should().Be("{\"status\":413,\"body\":\"html\"}");
		Newtonsoft.Json.JsonConvert.SerializeObject(response).Should().Be("{\"status\":413,\"body\":\"html\"}");
	}

	[TestCase(400, TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_For400_WhichAHandlerCanSend")]
	[TestCase(404, TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_For404_WhichAHandlerCanSend")]
	[TestCase(409, TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_For409_WhichAHandlerCanSend")]
	[TestCase(422, TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_For422_WhichAHandlerCanSend")]
	[TestCase(302, TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_For302_WhichCanFollowAProcessedPost")]
	[TestCase(303, TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_For303_WhichCanFollowAProcessedPost")]
	[TestCase(500, TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_For500")]
	[TestCase(502, TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_For502_WhichAGatewayCanSendAfterTheWrite")]
	[TestCase(200, TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_For200")]
	[TestCase(null, TestName = "IsRefusedWithoutProcessing_ShouldBeFalse_WhenTheStatusIsUnknown")]
	[Description("Every other status, and an unknown one, proves nothing about whether the request was processed")]
	public void IsRefusedWithoutProcessing_ShouldBeFalse(int? status) {
		new SiteHttpResponse(status, "").IsRefusedWithoutProcessing.Should().BeFalse();
	}

	#endregion

	#region Tests: page results

	[Test]
	[Description("The page results carry site-response only when they report a failure, under the agreed name")]
	public void PageResponses_ShouldCarryTheSiteResponse_OnlyWhenFailed() {
		// Arrange
		SiteResponse refusal = new(500, "Schema is locked", null, SiteResponse.JsonBody);
		Clio.Command.PageUpdateResponse failedUpdate = new() { Success = false, SiteResponse = refusal };
		Clio.Command.PageCreateResponse failedCreate = new() { Success = false, SiteResponse = refusal };

		// Act + Assert
		((ISiteResponseCarrier)failedUpdate).ReportsFailure.Should().BeTrue();
		((ISiteResponseCarrier)new Clio.Command.PageUpdateResponse { Success = true }).ReportsFailure.Should().BeFalse();
		((ISiteResponseCarrier)failedCreate).ReportsFailure.Should().BeTrue();
		JsonSerializer.Serialize(failedUpdate).Should().Contain("\"site-response\":{\"status\":500");
		JsonSerializer.Serialize(failedCreate).Should().Contain("\"site-response\":{\"status\":500");
		JsonSerializer.Serialize(new Clio.Command.PageUpdateResponse { Success = true }).Should()
			.NotContain("site-response", because: "a caller that ignores the field sees the result it saw before");
	}

	#endregion

}
