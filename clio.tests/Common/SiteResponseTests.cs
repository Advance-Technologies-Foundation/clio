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
