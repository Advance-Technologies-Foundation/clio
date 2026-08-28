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
	[Description("A keyed read body whose selected column is named ExceptionMessage (alongside @odata.context and Id) is data, not an ASP.NET error.")]
	public void TryDetect_Should_Not_Treat_Error_Named_Column_On_Keyed_Read_As_Error() {
		// Arrange
		const string body =
			"{\"@odata.context\":\"http://env/0/odata/$metadata#Contact\"," +
			"\"Id\":\"11111111-1111-1111-1111-111111111111\",\"ExceptionMessage\":\"boom at /home/depot\"}";
		JsonElement root = JsonDocument.Parse(body).RootElement;

		// Act
		bool isError = ODataResponseError.TryDetect(root, out _);

		// Assert
		isError.Should().BeFalse(because:
			"the body carries @odata.context and Id alongside the caller-chosen ExceptionMessage column, so it is a real keyed entity read, not a server error");
	}

	[Test]
	[Category("Unit")]
	[Description("A keyed read body whose selected columns are named ExceptionType and StackTrace (plus @odata.context/Id) is data, not an ASP.NET error.")]
	public void TryDetect_Should_Not_Treat_Error_Named_Columns_On_Keyed_Read_As_Error() {
		// Arrange
		const string body =
			"{\"@odata.context\":\"http://env/0/odata/$metadata#Log\"," +
			"\"Id\":\"22222222-2222-2222-2222-222222222222\",\"ExceptionType\":\"App.Exception\",\"StackTrace\":\"at App.X()\"}";
		JsonElement root = JsonDocument.Parse(body).RootElement;

		// Act
		bool isError = ODataResponseError.TryDetect(root, out _);

		// Assert
		isError.Should().BeFalse(because:
			"the body carries @odata.context and Id, so a log-shaped entity whose columns are named like HttpError keys is data, not a server error");
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