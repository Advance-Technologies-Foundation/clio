using System;
using System.Collections.Generic;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
[Property("Module", "Common")]
public class CreatioLicenseClientTests {

	private static (CreatioLicenseClient client, IApplicationClient applicationClient) CreateClient(bool isNetCore = false) {
		// Real ServiceUrlBuilder so the route mapping + framework prefix are exercised; only the I/O boundary is faked.
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = new ServiceUrlBuilder(new EnvironmentSettings {
			Uri = "http://localhost",
			IsNetCore = isNetCore
		});
		return (new CreatioLicenseClient(applicationClient, urlBuilder), applicationClient);
	}

	[Test]
	[Category("Unit")]
	[Description("Posts the operation code as a single-element licOperationCodes array to the WebApp-prefixed LicenseService path and maps the status to true.")]
	public void GetLicenseOperationStatuses_ShouldPostCodesAndMapGranted_WhenLicensed() {
		// Arrange
		(CreatioLicenseClient client, IApplicationClient applicationClient) = CreateClient(isNetCore: false);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"GetLicOperationStatusesResult\":{\"success\":true,\"licOperationStatuses\":[" +
				"{\"Key\":\"CanCustomizeBranding\",\"Value\":true}]}}");

		// Act
		IReadOnlyDictionary<string, bool> statuses = client.GetLicenseOperationStatuses(
			new[] { "CanCustomizeBranding" }, new CreatioRequestOptions());

		// Assert
		statuses.Should().ContainKey("CanCustomizeBranding").WhoseValue.Should()
			.BeTrue(because: "the status for the requested operation is true");
		applicationClient.Received(1).ExecutePostRequest(
			"http://localhost/0/ServiceModel/LicenseService.svc/GetLicOperationStatuses",
			"{\"licOperationCodes\":[\"CanCustomizeBranding\"]}", 100_000, 3, 1);
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an empty map when the response reports success=false (unlicensed caller).")]
	public void GetLicenseOperationStatuses_ShouldReturnEmptyMap_WhenResponseReportsFailure() {
		// Arrange
		(CreatioLicenseClient client, IApplicationClient applicationClient) = CreateClient();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"GetLicOperationStatusesResult\":{\"success\":false,\"licOperationStatuses\":[]}}");

		// Act
		IReadOnlyDictionary<string, bool> statuses = client.GetLicenseOperationStatuses(
			new[] { "CanCustomizeBranding" }, new CreatioRequestOptions());

		// Assert
		statuses.Should().BeEmpty(because: "a success=false payload yields no granted operations");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns an empty map without calling the service when no operation codes are requested.")]
	public void GetLicenseOperationStatuses_ShouldReturnEmptyMapWithoutCall_WhenNoCodesRequested() {
		// Arrange
		(CreatioLicenseClient client, IApplicationClient applicationClient) = CreateClient();

		// Act
		IReadOnlyDictionary<string, bool> statuses = client.GetLicenseOperationStatuses(
			Array.Empty<string>(), new CreatioRequestOptions());

		// Assert
		statuses.Should().BeEmpty(because: "an empty request maps to an empty result");
		applicationClient.DidNotReceive().ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Throws a diagnostic InvalidOperationException when the LicenseService response is a non-JSON body (e.g. an auth redirect).")]
	public void GetLicenseOperationStatuses_ShouldThrow_WhenResponseBodyIsNotParseableJson() {
		// Arrange
		(CreatioLicenseClient client, IApplicationClient applicationClient) = CreateClient();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("OK");

		// Act
		Action act = () => client.GetLicenseOperationStatuses(new[] { "CanCustomizeBranding" }, new CreatioRequestOptions());

		// Assert
		act.Should().Throw<InvalidOperationException>(because: "a non-JSON body signals the request never reached LicenseService")
			.WithMessage("*LicenseService*");
	}

	[Test]
	[Category("Unit")]
	[Description("An HTML body (a login redirect or a server error page) is named, never previewed: it can carry session cookies, request tokens and stack traces, and the message reaches the log and an agent transcript.")]
	public void GetLicenseOperationStatuses_ShouldNotPreviewTheBody_WhenResponseIsAnHtmlPage() {
		// Arrange
		(CreatioLicenseClient client, IApplicationClient applicationClient) = CreateClient();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("  <html><body>Request Error. __RequestVerificationToken=abc123</body></html>");

		// Act
		Action act = () => client.GetLicenseOperationStatuses(new[] { "CanCustomizeBranding" }, new CreatioRequestOptions());

		// Assert
		string message = act.Should().Throw<InvalidOperationException>(because: "an HTML page is not a service answer")
			.Which.Message;
		message.Should().Contain("an HTML page instead of JSON", because: "the caller is told what came back");
		message.Should().NotContain("RequestVerificationToken", because: "the page body is never shown");
		message.Should().NotContain("<html>", because: "no markup reaches the message");
	}

	[Test]
	[Category("Unit")]
	[Description("A body that is neither JSON nor HTML is previewed only after it is redacted, then capped: a credential in it never reaches the message.")]
	public void GetLicenseOperationStatuses_ShouldRedactThePreview_WhenResponseIsNotJson() {
		// Arrange
		(CreatioLicenseClient client, IApplicationClient applicationClient) = CreateClient();
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("Service unavailable; password=hunter2secret; retry later");

		// Act
		Action act = () => client.GetLicenseOperationStatuses(new[] { "CanCustomizeBranding" }, new CreatioRequestOptions());

		// Assert
		string message = act.Should().Throw<InvalidOperationException>(because: "the body is not JSON").Which.Message;
		message.Should().Contain("Service unavailable", because: "the readable part of the body is previewed");
		message.Should().NotContain("hunter2secret", because: "a credential in the body is redacted before the preview");
	}

	[Test]
	[Category("Unit")]
	[Description("The preview is redacted BEFORE it is capped: a JSON credential whose value the 200-character cap would slice — which the redactor cannot match once its closing quote is cut off — never reaches the message.")]
	public void GetLicenseOperationStatuses_ShouldRedactBeforeCapping_WhenACredentialCrossesThePreviewLimit() {
		// Arrange
		(CreatioLicenseClient client, IApplicationClient applicationClient) = CreateClient();
		// Not JSON as a whole, so the body is previewed; the credential's value starts ten characters before the cap.
		string body = "Unavailable " + new string('x', 165) + "{\"password\":\"zq9Kx7secretvalue\"} retry later";
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(body);

		// Act
		Action act = () => client.GetLicenseOperationStatuses(new[] { "CanCustomizeBranding" }, new CreatioRequestOptions());

		// Assert
		string message = act.Should().Throw<InvalidOperationException>(because: "the body is not JSON").Which.Message;
		message.Should().Contain("Unavailable", because: "the readable part of the body is previewed");
		message.Should().NotContain("zq9K",
			because: "capping first would cut the value's closing quote off, and the redactor leaves such a value as it is");
	}

	[Test]
	[Category("Unit")]
	[Description("The preview of a body that is not JSON is capped at 200 characters: text past the cap never reaches the message, the log or an agent transcript.")]
	public void GetLicenseOperationStatuses_ShouldCapThePreview_WhenTheBodyIsLong() {
		// Arrange
		(CreatioLicenseClient client, IApplicationClient applicationClient) = CreateClient();
		string body = "Unavailable " + new string('x', 250) + " PASTTHECAPMARKER " + new string('y', 700);
		applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(),
				Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(body);

		// Act
		Action act = () => client.GetLicenseOperationStatuses(new[] { "CanCustomizeBranding" }, new CreatioRequestOptions());

		// Assert
		string message = act.Should().Throw<InvalidOperationException>(because: "the body is not JSON").Which.Message;
		message.Should().Contain("Unavailable", because: "the start of the body is previewed");
		message.Should().NotContain("PASTTHECAPMARKER", because: "the preview stops at 200 characters");
	}
}
