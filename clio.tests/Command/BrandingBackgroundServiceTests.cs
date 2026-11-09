using System;
using Clio.Command;
using Clio.Command.Branding;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class BrandingBackgroundServiceTests {

	private static readonly Guid ImageId = Guid.Parse("6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81");

	private IApplicationClient _applicationClient;
	private IFeatureStateService _featureState;
	private BrandingBackgroundService _service;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_featureState = Substitute.For<IFeatureStateService>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(call => call.Arg<string>());
		_service = new BrandingBackgroundService(_applicationClient, urlBuilder, _featureState);
	}

	[Test]
	[Description("Asks the OData SysImage collection for the one row whose Id is the image id.")]
	public void TryFindUploadedImage_Should_Query_SysImage_By_The_Image_Id() {
		_applicationClient.ExecuteGetRequest(default).ReturnsForAnyArgs("{\"value\":[]}");

		_service.TryFindUploadedImage(ImageId, out _, out _);

		_applicationClient.Received(1).ExecuteGetRequest($"odata/SysImage?$filter=Id eq {ImageId}&$select=Id&$top=1");
	}

	[TestCase("{\"value\":[{\"Id\":\"6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81\"}]}", true)]
	[TestCase("{\"value\":[]}", false)]
	[Description("Reports whether the environment has the uploaded image once the check itself succeeds.")]
	public void TryFindUploadedImage_Should_Report_Whether_The_Image_Exists(string response, bool expected) {
		_applicationClient.ExecuteGetRequest(default).ReturnsForAnyArgs(response);

		bool checkedImage = _service.TryFindUploadedImage(ImageId, out bool exists, out _);

		checkedImage.Should().BeTrue(because: "an OData answer with a value collection is a completed check");
		exists.Should().Be(expected, because: "only a returned row proves the image was uploaded");
	}

	[TestCase("not json", "the environment returned a non-JSON response.")]
	[TestCase("", "the environment returned an empty response.")]
	[TestCase("{\"rows\":[]}", "the environment returned an unexpected response.")]
	[Description("Reports a check that could not complete, with the reason, instead of a missing image.")]
	public void TryFindUploadedImage_Should_Report_A_Check_That_Failed(string response, string expectedError) {
		_applicationClient.ExecuteGetRequest(default).ReturnsForAnyArgs(response);

		bool checkedImage = _service.TryFindUploadedImage(ImageId, out _, out string error);

		checkedImage.Should().BeFalse(because: "an unreadable answer does not say whether the image exists");
		error.Should().Be(expectedError, because: "the caller must learn why the check failed");
	}

	[Test]
	[Description("Reports the message of a request that throws as a failed check.")]
	public void TryFindUploadedImage_Should_Report_The_Message_When_The_Request_Throws() {
		_applicationClient.ExecuteGetRequest(default).ThrowsForAnyArgs(new InvalidOperationException("connection reset"));

		bool checkedImage = _service.TryFindUploadedImage(ImageId, out _, out string error);

		checkedImage.Should().BeFalse(because: "a request that never answered checked nothing");
		error.Should().Be("connection reset", because: "the request failure is the reason the check failed");
	}

	[Test]
	[Description("Turns the UsePanelIconBackground feature off for All employees.")]
	public void TryTurnOffPanelIconBackground_Should_Turn_The_Feature_Off_For_All_Employees() {
		bool turnedOff = _service.TryTurnOffPanelIconBackground("The theme background", out _);

		turnedOff.Should().BeTrue(because: "the feature write succeeded");
		_featureState.Received(1).SetFeatureState("UsePanelIconBackground", SysAdminUnitIds.AllEmployees, false);
	}

	[Test]
	[Description("Turns a failed feature write into a warning that names what was applied and why the panel may still hide it.")]
	public void TryTurnOffPanelIconBackground_Should_Turn_A_Failure_Into_A_Warning() {
		_featureState.WhenForAnyArgs(service => service.SetFeatureState(default, default, default, default))
			.Do(_ => throw new InvalidOperationException("feature service unavailable"));

		bool turnedOff = _service.TryTurnOffPanelIconBackground("The background image", out string warning);

		turnedOff.Should().BeFalse(because: "the feature write failed");
		warning.Should().Be("The background image was applied, but turning off the UsePanelIconBackground feature " +
			"failed, so the panel may still hide it: feature service unavailable",
			because: "the background is already applied, so the failure is reported as a warning about it");
	}
}
