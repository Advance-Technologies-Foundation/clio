using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Package;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Package;

/// <summary>
/// Covers ENG-93365: every non-JSON response body must surface as a typed error naming the endpoint,
/// never as a raw <c>System.Text.Json</c> parser message.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public class ServiceResponseJsonGuardTests
{
	private const string Url = "http://localhost/0/DataService/json/SyncReply/SelectQuery";
	private const string RawParserFragment = "is an invalid start of a value";

	private static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	[Test]
	[Description("Deserialize returns the parsed response when the body is valid JSON.")]
	public void Deserialize_ReturnsParsedResponse_WhenBodyIsValidJson() {
		// Arrange
		const string body = """{"success":true}""";

		// Act
		ProbeResponse response = ServiceResponseJsonGuard.Deserialize<ProbeResponse>(
			"SelectQuery", Url, body, JsonOptions);

		// Assert
		response.Success.Should().BeTrue(
			"a valid JSON body must still deserialize through the guard unchanged");
	}

	[Test]
	[Description("Deserialize throws a typed error naming the endpoint when the body is an HTML page.")]
	public void Deserialize_ThrowsTypedError_WhenBodyIsHtmlPage() {
		// Arrange
		const string body = "<!DOCTYPE html><html><body>Runtime Error</body></html>";

		// Act
		Action act = () => ServiceResponseJsonGuard.Deserialize<ProbeResponse>(
			"SelectQuery", Url, body, JsonOptions);

		// Assert
		InvalidOperationException exception = act.Should().Throw<InvalidOperationException>(
			"an HTML body is a reportable endpoint failure, not a parser detail")
			.Which;
		exception.Message.Should().Contain("HTML page instead of JSON",
			"the message must name the actual failure so the caller can act on it");
		exception.Message.Should().Contain(Url,
			"the message must name the endpoint the body came from");
		exception.Message.Should().NotContain(RawParserFragment,
			"the raw System.Text.Json parser text must never reach the caller (ENG-93365)");
		exception.InnerException.Should().BeOfType<JsonException>(
			"the parser failure is preserved as the inner exception for diagnostics");
	}

	[Test]
	[Description("Deserialize omits the HTML body from the message so a login or error page cannot leak tokens.")]
	public void Deserialize_OmitsHtmlBody_WhenBodyIsLoginPage() {
		// Arrange
		const string body = "<html><head><title>Log in</title></head><body>token=super-secret-value</body></html>";

		// Act
		Action act = () => ServiceResponseJsonGuard.Deserialize<ProbeResponse>(
			"SelectQuery", Url, body, JsonOptions);

		// Assert
		string message = act.Should().Throw<InvalidOperationException>().Which.Message;
		message.Should().NotContain("super-secret-value",
			"an HTML error or login page can carry session tokens, so its body is never previewed");
		message.Should().NotContain("<html",
			"no HTML markup from the response body belongs in the surfaced message");
	}

	[Test]
	[Description("Deserialize throws a typed error with a response preview when the body is truncated JSON.")]
	public void Deserialize_ThrowsTypedErrorWithPreview_WhenBodyIsTruncatedJson() {
		// Arrange — starts with '{', so a first-byte check would miss it and the raw parser message would leak.
		const string body = """{"success":tr""";

		// Act
		Action act = () => ServiceResponseJsonGuard.Deserialize<ProbeResponse>(
			"SelectQuery", Url, body, JsonOptions);

		// Assert
		string message = act.Should().Throw<InvalidOperationException>(
			"a truncated body is unparseable and must be reported as such")
			.Which.Message;
		message.Should().Contain("unparseable response",
			"the message must state that the body could not be parsed");
		message.Should().Contain(Url, "the message must name the endpoint");
		message.Should().Contain("Response preview:",
			"the caller needs to see what the endpoint actually returned");
		message.Should().Contain(body, "the preview must carry the real body for a short response");
	}

	[Test]
	[Description("Deserialize caps the response preview so a large body cannot flood the error message.")]
	public void Deserialize_CapsResponsePreview_WhenBodyIsLarge() {
		// Arrange
		string body = new string('x', 5000);

		// Act
		Action act = () => ServiceResponseJsonGuard.Deserialize<ProbeResponse>(
			"SelectQuery", Url, body, JsonOptions);

		// Assert
		string message = act.Should().Throw<InvalidOperationException>().Which.Message;
		message.Length.Should().BeLessThan(1000,
			"the preview is bounded, so a large body never floods the surfaced message");
		message.Should().Contain("…",
			"a truncated preview is marked with an ellipsis so the caller knows it was cut");
	}

	[Test]
	[Description("Deserialize redacts credential values found in a non-JSON body preview.")]
	public void Deserialize_RedactsCredentials_WhenPreviewCarriesSecrets() {
		// Arrange
		const string body = "Unexpected failure password=hunter2 at data source";

		// Act
		Action act = () => ServiceResponseJsonGuard.Deserialize<ProbeResponse>(
			"SelectQuery", Url, body, JsonOptions);

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.Which.Message.Should().NotContain("hunter2",
				"the preview is redacted at construction time because the CLI path logs the message with no redactor");
	}

	[Test]
	[Description("Deserialize throws NonJsonServiceResponseException so soft-degrading callers can tell a non-JSON body from a server rejection.")]
	public void Deserialize_ThrowsNonJsonServiceResponseException_WhenBodyIsNotJson() {
		// Arrange
		const string body = "<html><body>Runtime Error</body></html>";

		// Act
		Action act = () => ServiceResponseJsonGuard.Deserialize<ProbeResponse>(
			"SelectQuery", Url, body, JsonOptions);

		// Assert
		act.Should().Throw<NonJsonServiceResponseException>(
			"a caller that degrades softly must distinguish an unusable body from a rejected request")
			.Which.Should().BeAssignableTo<InvalidOperationException>(
				"the type derives from InvalidOperationException so existing catch clauses keep working");
	}

	[Test]
	[Description("Deserialize throws a typed error naming the endpoint when the body is empty.")]
	public void Deserialize_ThrowsTypedError_WhenBodyIsEmpty() {
		// Act
		Action act = () => ServiceResponseJsonGuard.Deserialize<ProbeResponse>(
			"SelectQuery", Url, string.Empty, JsonOptions);

		// Assert
		string message = act.Should().Throw<InvalidOperationException>(
			"an empty body cannot satisfy the request and must be reported")
			.Which.Message;
		message.Should().Contain("empty response", "the message must state that no body was returned");
		message.Should().Contain(Url, "the message must name the endpoint");
	}

	[Test]
	[Description("Deserialize throws a typed error naming the endpoint when the body is the JSON literal null.")]
	public void Deserialize_ThrowsTypedError_WhenBodyDeserializesToNull() {
		// Act
		Action act = () => ServiceResponseJsonGuard.Deserialize<ProbeResponse>(
			"SelectQuery", Url, "null", JsonOptions);

		// Assert
		act.Should().Throw<InvalidOperationException>(
			"a body that parses to null carries no response to work with")
			.Which.Message.Should().Contain("empty response",
				"a null payload is reported the same way as a missing body");
	}

	private sealed class ProbeResponse
	{
		[JsonPropertyName("success")]
		public bool Success { get; set; }
	}
}
