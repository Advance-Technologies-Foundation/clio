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
}
