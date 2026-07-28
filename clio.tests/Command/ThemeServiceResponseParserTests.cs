namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Command.Theming;
using FluentAssertions;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class ThemeServiceResponseParserTests
{
	[Test]
	[Category("Unit")]
	[Description("Reports a failure and surfaces errorInfo.message when the response carries an explicit success=false with an error block.")]
	public void TryGetFailure_ShouldReportFailureWithMessage_WhenSuccessFalseAndErrorInfoPresent() {
		// Arrange
		const string response = "{\"success\":false,\"errorInfo\":{\"errorCode\":\"SecurityException\",\"message\":\"no permission\"}}";

		// Act
		bool failed = ThemeServiceResponseParser.TryGetFailure(response, out string errorMessage);

		// Assert
		failed.Should().BeTrue(because: "an explicit success=false is a failure");
		errorMessage.Should().Be("no permission", because: "the server-provided message must be surfaced");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports a failure with a null message when success=false but no errorInfo block is present.")]
	public void TryGetFailure_ShouldReportFailureWithNullMessage_WhenSuccessFalseAndNoErrorInfo() {
		// Act
		bool failed = ThemeServiceResponseParser.TryGetFailure("{\"success\":false}", out string errorMessage);

		// Assert
		failed.Should().BeTrue(because: "success=false must fail even without an errorInfo block");
		errorMessage.Should().BeNull(because: "no message was provided by the server");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports no failure when the response carries success=true.")]
	public void TryGetFailure_ShouldReportNoFailure_WhenSuccessTrue() {
		// Act
		bool failed = ThemeServiceResponseParser.TryGetFailure("{\"success\":true}", out string errorMessage);

		// Assert
		failed.Should().BeFalse(because: "success=true is not a failure");
		errorMessage.Should().BeNull(because: "a successful response carries no error");
	}

	[Test]
	[Category("Unit")]
	[TestCase("", TestName = "TryGetFailure tolerates an empty body")]
	[TestCase("   ", TestName = "TryGetFailure tolerates a whitespace body")]
	[Description("Treats an empty body as success (the contract default), so a minimal success response is not misread as a failure.")]
	public void TryGetFailure_ShouldReportNoFailure_WhenBodyEmpty(string response) {
		// Act
		bool failed = ThemeServiceResponseParser.TryGetFailure(response, out string errorMessage);

		// Assert
		failed.Should().BeFalse(because: "an empty body is the contract default for a successful write");
		errorMessage.Should().BeNull(because: "no failure means no error message");
	}

	[Test]
	[Category("Unit")]
	[TestCase("OK", TestName = "TryGetFailure fails on a plain non-JSON body")]
	[TestCase("<html><body>Sign in</body></html>", TestName = "TryGetFailure fails on an HTML login page")]
	[Description("Reports a failure when a non-empty body is not valid JSON: ThemeService always answers with a JSON BaseResponse, so a non-JSON payload means the request hit an auth redirect or proxy error and never reached the service.")]
	public void TryGetFailure_ShouldReportFailure_WhenBodyIsNonEmptyNonJson(string response) {
		// Act
		bool failed = ThemeServiceResponseParser.TryGetFailure(response, out string errorMessage);

		// Assert
		failed.Should().BeTrue(because: "a non-JSON body signals the request did not reach ThemeService and must not be reported as success");
		errorMessage.Should().Contain("Unexpected response from server",
			because: "the parser mirrors delete-package's unexpected-response diagnostic");
		errorMessage.Should().Contain(response,
			because: "the unexpected body must be surfaced so the caller can see what actually came back");
	}

	[Test]
	[Category("Unit")]
	[Description("Strips control characters from an unexpected non-JSON body before echoing it, so a hostile or misbehaving endpoint cannot inject fake output lines or terminal escape sequences into user-facing / MCP output.")]
	public void TryGetFailure_ShouldStripControlCharactersFromEchoedBody_WhenBodyIsNonJson() {
		// Arrange
		const string response = "line1\r\nFAKE SUCCESS\n[31mred[0m";

		// Act
		bool failed = ThemeServiceResponseParser.TryGetFailure(response, out string errorMessage);

		// Assert
		failed.Should().BeTrue(because: "a non-JSON body signals the request did not reach ThemeService");
		errorMessage.Should().NotContain("\n", because: "newlines that would forge extra output lines must be neutralised");
		errorMessage.Should().NotContain("\r", because: "carriage returns that would forge extra output lines must be neutralised");
		errorMessage.Should().NotContain("", because: "ANSI escape sequences must be neutralised before reaching a terminal");
		errorMessage.Should().Contain("line1 ", because: "the visible content of the body must still be surfaced with control characters replaced by spaces");
	}

	[Test]
	[Category("Unit")]
	[Description("Caps an oversized non-JSON body so a large payload (e.g. a whole HTML page) cannot flood user-facing / MCP output.")]
	public void TryGetFailure_ShouldTruncateEchoedBody_WhenBodyExceedsLengthCap() {
		// Arrange
		string response = new('a', 5000);

		// Act
		bool failed = ThemeServiceResponseParser.TryGetFailure(response, out string errorMessage);

		// Assert
		failed.Should().BeTrue(because: "a non-JSON body signals the request did not reach ThemeService");
		errorMessage.Should().EndWith("...", because: "a truncated body must be marked as elided");
		errorMessage.Length.Should().BeLessThan(response.Length,
			because: "an oversized body must be capped rather than echoed in full");
	}
}
