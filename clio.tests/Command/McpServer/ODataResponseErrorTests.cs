using System.Text.Json;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ODataResponseErrorTests {
	[Test]
	[Category("Unit")]
	[Description("An ASP.NET HttpError carrying InnerException is still detected. ASP.NET Web API populates InnerException whenever error detail is enabled, so any 'does the body carry other members?' guard on this branch would report a genuine server exception as success through every caller of TryDetect.")]
	public void TryDetect_Should_Detect_AspNet_Error_Carrying_InnerException() {
		// Arrange
		const string body =
			"{\"Message\":\"An error has occurred.\",\"ExceptionMessage\":\"NullReferenceException at App.X\"," +
			"\"ExceptionType\":\"System.NullReferenceException\",\"StackTrace\":\"at App.X()\"," +
			"\"InnerException\":{\"Message\":\"inner\",\"ExceptionType\":\"System.Exception\"}}";
		JsonElement root = JsonDocument.Parse(body).RootElement;

		// Act
		bool isError = ODataResponseError.TryDetect(root, out string message);

		// Assert
		isError.Should().BeTrue(because:
			"InnerException is a standard member of the HttpError shape, not evidence that the body is data");
		message.Should().Contain("NullReferenceException", because:
			"the caller needs the actual exception text, not a generic server-error placeholder");
	}

	[Test]
	[Category("Unit")]
	[Description("An ASP.NET HttpError whose members are all present is detected regardless of extra ModelState-style members, for the same reason as InnerException.")]
	public void TryDetect_Should_Detect_AspNet_Error_Carrying_ModelState() {
		// Arrange
		const string body =
			"{\"Message\":\"The request is invalid.\",\"ExceptionMessage\":\"validation failed\"," +
			"\"ModelState\":{\"data.Name\":[\"required\"]}}";
		JsonElement root = JsonDocument.Parse(body).RootElement;

		// Act
		bool isError = ODataResponseError.TryDetect(root, out _);

		// Assert
		isError.Should().BeTrue(because:
			"an HttpError with additional diagnostic members is still an error body, and reporting it as success is the exact defect this class exists to prevent");
	}

	[Test]
	[Category("Unit")]
	[Description("A bare ASP.NET HttpError body (only error keys, no OData members) is still detected as an error.")]
	public void TryDetect_Should_Detect_Bare_AspNet_Error_Envelope() {
		// Arrange
		const string body =
			"{\"Message\":\"An error occurred while processing.\"," +
			"\"ExceptionMessage\":\"NullReferenceException at App.X\",\"ExceptionType\":\"System.NullReferenceException\"," +
			"\"StackTrace\":\"at App.X()\"}";
		JsonElement root = JsonDocument.Parse(body).RootElement;

		// Act
		bool isError = ODataResponseError.TryDetect(root, out string message);

		// Assert
		isError.Should().BeTrue(because:
			"the body carries only HttpError keys, which is the recognized ASP.NET exception shape");
		message.Should().Contain("NullReferenceException",
			because: "the extracted exception text is surfaced for diagnosis");
	}

	[Test]
	[Category("Unit")]
	[Description("A bare routing error body (Message + MessageDetail only) is still detected as an error.")]
	public void TryDetect_Should_Detect_Bare_Routing_Error_Envelope() {
		// Arrange
		const string body =
			"{\"Message\":\"No HTTP resource was found that matches the request URI.\"," +
			"\"MessageDetail\":\"No type was found that matches the controller named 'Foo'\"}";
		JsonElement root = JsonDocument.Parse(body).RootElement;

		// Act
		bool isError = ODataResponseError.TryDetect(root, out string message);

		// Assert
		isError.Should().BeTrue(because: "the body carries only the routing-error keys, which is the recognized routing shape");
		message.Should().Contain("controller named 'Foo'",
			because: "the most specific routing detail is surfaced");
	}
}
