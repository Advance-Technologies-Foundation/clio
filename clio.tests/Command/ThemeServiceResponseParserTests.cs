namespace Clio.Tests.Command;

using Clio.Command;
using FluentAssertions;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public class ThemeServiceResponseParserTests
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
	[TestCase("OK", TestName = "TryGetFailure tolerates a non-JSON body")]
	[Description("Treats an empty or non-JSON body as success to avoid false negatives if the contract evolves.")]
	public void TryGetFailure_ShouldReportNoFailure_WhenBodyEmptyOrNonJson(string response) {
		// Act
		bool failed = ThemeServiceResponseParser.TryGetFailure(response, out string errorMessage);

		// Assert
		failed.Should().BeFalse(because: "a body that cannot be parsed as a failure response must not be misread as a failure");
		errorMessage.Should().BeNull(because: "no failure means no error message");
	}
}
