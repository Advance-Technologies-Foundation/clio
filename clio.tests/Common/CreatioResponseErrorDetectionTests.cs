using System;
using System.Text;
using System.Text.Json;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public sealed class CreatioResponseErrorDetectionTests {
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
		bool isError = CreatioResponseError.TryDetect(root, CreatioResponseContext.ODataPayload, out string message);

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
		bool isError = CreatioResponseError.TryDetect(root, CreatioResponseContext.ODataPayload, out _);

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
		bool isError = CreatioResponseError.TryDetect(root, CreatioResponseContext.ODataPayload, out string message);

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
		bool isError = CreatioResponseError.TryDetect(root, CreatioResponseContext.ODataPayload, out string message);

		// Assert
		isError.Should().BeTrue(because: "the body carries only the routing-error keys, which is the recognized routing shape");
		message.Should().Contain("controller named 'Foo'",
			because: "the most specific routing detail is surfaced");
	}

	// One large OData v4 error body, shared by the two tests below: no string `message` member, so the
	// message-producing path falls back to copying the whole `error` subtree out with GetRawText().
	private static string BuildLargeODataErrorBody() {
		StringBuilder builder = new();
		builder.Append("{\"error\":{\"code\":\"500\",\"details\":[");
		for (int index = 0; index < 4000; index++) {
			if (index > 0) {
				builder.Append(',');
			}
			builder.Append("{\"target\":\"field").Append(index).Append("\",\"detail\":\"")
				.Append('x', 250).Append("\"}");
		}
		builder.Append("]}}");
		return builder.ToString();
	}

	[Test]
	[Category("Unit")]
	[Description("Classifies a large OData v4 error body without materializing the error subtree, so a read that only needs the routing kind does not allocate the prose it discards.")]
	public void TryClassify_Should_Not_Materialize_The_Error_Subtree_For_A_Large_Body() {
		// Arrange
		//Deliberately synchronous: GC.GetAllocatedBytesForCurrentThread() counts the CURRENT thread, so an
		//await between the two reads could resume on a different pool thread and measure nothing.
		string body = BuildLargeODataErrorBody();
		JsonElement root = JsonDocument.Parse(body).RootElement;
		//One throwaway call so JIT and first-use allocations are not charged to the measured one.
		CreatioResponseError.TryClassify(root, CreatioResponseContext.ODataPayload, out bool _);
		long before = GC.GetAllocatedBytesForCurrentThread();

		// Act
		bool isError = CreatioResponseError.TryClassify(root, CreatioResponseContext.ODataPayload,
			out bool isUnregisteredEntity);
		long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		// Assert
		isError.Should().BeTrue(because: "an OData v4 error envelope is a recognized error whatever its size");
		isUnregisteredEntity.Should().BeFalse(because: "an OData v4 error is not the routing miss the hint applies to");
		allocated.Should().BeLessThan(body.Length,
			because: "classification must not copy the error subtree out of the document to decide a kind");
	}

	[Test]
	[Category("Unit")]
	[Description("Still returns the raw error subtree as the message for the same body, because the write callers report that text to the user.")]
	public void TryDetect_Should_Still_Return_The_Raw_Error_Subtree_When_There_Is_No_Message_Member() {
		// Arrange
		string body = BuildLargeODataErrorBody();
		JsonElement root = JsonDocument.Parse(body).RootElement;

		// Act
		bool isError = CreatioResponseError.TryDetect(root, CreatioResponseContext.ODataPayload, out string message);

		// Assert
		isError.Should().BeTrue(because: "the detection decision is the same on both contracts");
		message.Should().Contain("field3999",
			because: "a write caller reports the server's own error detail and must keep getting the whole subtree");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports the unregistered-entity routing miss as a kind produced during detection, not by searching the formatted message for the hint wording.")]
	public void TryClassify_Should_Report_The_Routing_Miss_Without_Reading_The_Hint_Text() {
		// Arrange
		const string routingMissBody =
			"{\"Message\":\"No HTTP resource was found that matches the request URI.\"," +
			"\"MessageDetail\":\"No type was found that matches the controller named 'Foo'\"}";
		const string otherErrorBody = "{\"Message\":\"Something else went wrong.\"}";

		// Act
		bool routingMissDetected = CreatioResponseError.TryClassify(
			JsonDocument.Parse(routingMissBody).RootElement, CreatioResponseContext.ODataPayload,
			out bool routingMissIsUnregistered);
		bool otherDetected = CreatioResponseError.TryClassify(
			JsonDocument.Parse(otherErrorBody).RootElement, CreatioResponseContext.ODataPayload,
			out bool otherIsUnregistered);

		// Assert
		routingMissDetected.Should().BeTrue(because: "the bare routing shape is a recognized OData error");
		routingMissIsUnregistered.Should().BeTrue(
			because: "the routing-miss wording is what the locally authored retry hint applies to");
		otherDetected.Should().BeTrue(because: "a bare Message body is still an error on an OData endpoint");
		otherIsUnregistered.Should().BeFalse(
			because: "an unrelated failure must not be reported as the asynchronous-rebuild wait");
	}
}
