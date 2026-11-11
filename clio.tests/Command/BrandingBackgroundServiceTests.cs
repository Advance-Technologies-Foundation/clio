using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

	private const string BindingSuffix = "ThemeBackground_d53a816b65ed4b5bad3bf739280add45";

	private static readonly Guid ImageId = Guid.Parse("6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81");

	private static readonly Guid MembershipId = Guid.Parse("8b1c2d3e-4f50-4a6b-9c7d-e8f9a0b1c2d3");

	private static readonly BackgroundGalleryImage GalleryImage =
		new(ImageId, MembershipId, BrandingBackgroundService.ShellBackgroundTagId);

	private IApplicationClient _applicationClient;
	private IFeatureStateService _featureState;
	private IPackageDataBinder _binder;
	private BrandingBackgroundService _service;

	[SetUp]
	public void SetUp() {
		_applicationClient = Substitute.For<IApplicationClient>();
		_featureState = Substitute.For<IFeatureStateService>();
		_binder = Substitute.For<IPackageDataBinder>();
		_binder.BindRow(default, default, default, default).ReturnsForAnyArgs(PackageDataBindingOutcome.Success());
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(call => call.Arg<string>());
		_service = new BrandingBackgroundService(_applicationClient, urlBuilder, _featureState);
	}

	[Test]
	[Description("Confirms an uploaded image after asking the OData SysImage collection for the one row whose Id is the image id.")]
	public void TryConfirmUploadedImage_Should_Confirm_An_Image_The_Environment_Has() {
		// Arrange
		ArrangeImageLookup($"{{\"value\":[{{\"Id\":\"{ImageId}\"}}]}}");

		// Act
		bool confirmed = _service.TryConfirmUploadedImage(ImageId, out string error);

		// Assert
		confirmed.Should().BeTrue(because: $"the environment returned the image row (error: {error})");
		_applicationClient.Received(1).ExecuteGetRequest($"odata/SysImage?$filter=Id eq {ImageId}&$select=Id&$top=1");
	}

	[TestCase("{\"value\":[]}", "No uploaded image with id '6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81' was found in the " +
		"environment. Upload the file first with upload-image and pass the id it prints.")]
	[TestCase("not json", "Could not check the image in the environment: the environment returned a non-JSON response.")]
	[TestCase("", "Could not check the image in the environment: the environment returned an empty response.")]
	[TestCase("{\"rows\":[]}", "Could not check the image in the environment: the environment returned an unexpected response.")]
	[Description("Refuses an image id the environment has no SysImage row for, and an image check that could not complete, with the reason.")]
	public void TryConfirmUploadedImage_Should_Refuse_An_Image_It_Cannot_Confirm(string response, string expectedError) {
		// Arrange
		ArrangeImageLookup(response);

		// Act
		bool confirmed = _service.TryConfirmUploadedImage(ImageId, out string error);

		// Assert
		confirmed.Should().BeFalse(because: "only an image the environment confirmed may become a background");
		error.Should().Be(expectedError, because: "the caller must learn whether the image is missing or the check failed");
	}

	[Test]
	[Description("Lets a cancelled image check through instead of reporting it as a check that failed.")]
	public void TryConfirmUploadedImage_Should_Let_A_Cancellation_Through() {
		// Arrange
		_applicationClient.ExecuteGetRequest(default).ThrowsForAnyArgs(new OperationCanceledException());

		// Act
		Action confirm = () => _service.TryConfirmUploadedImage(ImageId, out _);

		// Assert
		confirm.Should().Throw<OperationCanceledException>(
			because: "a cancelled call is the caller's decision, not a refusal of the image");
	}

	[Test]
	[Description("Adds an image that is already in the gallery without posting a membership, and reports its membership.")]
	public void TryAddImageToBackgroundGallery_Should_Return_The_Existing_Membership() {
		// Arrange
		ArrangeMembershipReads($"{{\"value\":[{{\"Id\":\"{MembershipId}\"}}]}}");

		// Act
		bool added = _service.TryAddImageToBackgroundGallery(ImageId, out BackgroundGalleryImage image, out string error);

		// Assert
		added.Should().BeTrue(because: $"an image that is already in the gallery is added (error: {error})");
		image.Should().Be(GalleryImage, because: "the caller binds exactly this membership row");
		_applicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default);
	}

	[Test]
	[Description("Adds the image when the gallery registration request times out but the membership row landed, because the re-read decides.")]
	public void TryAddImageToBackgroundGallery_Should_Add_The_Image_When_The_Registration_Times_Out_But_Lands() {
		// Arrange
		ArrangeMembershipReads("{\"value\":[]}", $"{{\"value\":[{{\"Id\":\"{MembershipId}\"}}]}}");
		_applicationClient.ExecutePostRequest("odata/SysImageInTag", Arg.Any<string>())
			.Throws(new AggregateException(new TaskCanceledException("A task was canceled.")));

		// Act
		bool added = _service.TryAddImageToBackgroundGallery(ImageId, out BackgroundGalleryImage image, out string error);

		// Assert
		added.Should().BeTrue(because: $"the membership row is in the gallery although the POST timed out (error: {error})");
		image.Should().Be(GalleryImage, because: "the re-read found the membership row");
	}

	[Test]
	[Description("Lets a cancelled gallery registration through instead of reporting it as a registration that failed.")]
	public void TryAddImageToBackgroundGallery_Should_Let_A_Cancellation_Through() {
		// Arrange
		ArrangeMembershipReads("{\"value\":[]}");
		_applicationClient.ExecutePostRequest("odata/SysImageInTag", Arg.Any<string>())
			.Throws(new OperationCanceledException());

		// Act
		Action add = () => _service.TryAddImageToBackgroundGallery(ImageId, out _, out _);

		// Assert
		add.Should().Throw<OperationCanceledException>(
			because: "a cancelled call is the caller's decision, not a gallery refusal");
	}

	[Test]
	[Description("Turns the UsePanelIconBackground feature off for All employees.")]
	public void TryTurnOffPanelIconBackground_Should_Turn_The_Feature_Off_For_All_Employees() {
		// Act
		bool turnedOff = _service.TryTurnOffPanelIconBackground("The theme background", out _);

		// Assert
		turnedOff.Should().BeTrue(because: "the feature write succeeded");
		_featureState.Received(1).SetFeatureState("UsePanelIconBackground", SysAdminUnitIds.AllEmployees, false);
	}

	[Test]
	[Description("Turns a failed feature write into a warning that names what was applied and why the panel may still hide it.")]
	public void TryTurnOffPanelIconBackground_Should_Turn_A_Failure_Into_A_Warning() {
		// Arrange
		_featureState.WhenForAnyArgs(service => service.SetFeatureState(default, default, default, default))
			.Do(_ => throw new InvalidOperationException("feature service unavailable"));

		// Act
		bool turnedOff = _service.TryTurnOffPanelIconBackground("The background image", out string warning);

		// Assert
		turnedOff.Should().BeFalse(because: "the feature write failed");
		warning.Should().Be("The background image was applied, but turning off the UsePanelIconBackground feature " +
			"failed, so the panel may still hide it: feature service unavailable",
			because: "the background is already applied, so the failure is reported as a warning about it");
	}

	[Test]
	[Description("Lets a cancelled feature write through instead of reporting it as a warning.")]
	public void TryTurnOffPanelIconBackground_Should_Let_A_Cancellation_Through() {
		// Arrange
		_featureState.WhenForAnyArgs(service => service.SetFeatureState(default, default, default, default))
			.Do(_ => throw new OperationCanceledException());

		// Act
		Action turnOff = () => _service.TryTurnOffPanelIconBackground("The theme background", out _);

		// Assert
		turnOff.Should().Throw<OperationCanceledException>(
			because: "a cancelled call is the caller's decision, not a failed turn-off to warn about");
	}

	[Test]
	[Description("Binds the image and then its gallery membership through the given binder under the given binding name, without selecting a package and without binding anything else.")]
	public void TryBindGalleryImage_Should_Bind_The_Image_And_Then_Its_Membership() {
		// Arrange
		List<string> bound = [];
		List<string> warnings = [];

		// Act
		bool boundAll = _service.TryBindGalleryImage(_binder, GalleryImage, BindingSuffix, bound, warnings,
			out string error);

		// Assert
		boundAll.Should().BeTrue(because: $"every binding step succeeded (error: {error})");
		bound.Should().Equal([BrandingBackgroundService.ImagePart, BrandingBackgroundService.GalleryMembershipPart],
			because: "the image and its gallery membership are the parts every background delivers");
		warnings.Should().BeEmpty(because: "the binder refused nothing");
		Received.InOrder(() => {
			_binder.BindRow("SysImage", BindingSuffix, Arg.Any<IReadOnlyList<string>>(), ImageId);
			_binder.BindRow("SysImageInTag", BindingSuffix, Arg.Any<IReadOnlyList<string>>(), MembershipId);
		});
		_binder.DidNotReceiveWithAnyArgs().UsePackage(default);
		_binder.DidNotReceiveWithAnyArgs().BindSysSettingsValue(default);
		_binder.DidNotReceiveWithAnyArgs().BindFeatureOffState(default);
	}

	[Test]
	[Description("Does not bind the gallery membership when the image is not bound, and passes on why.")]
	public void TryBindGalleryImage_Should_Not_Bind_The_Membership_Of_An_Unbound_Image() {
		// Arrange
		_binder.BindRow("SysImage", Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<Guid>()).Returns(
			PackageDataBindingOutcome.Refused(["SysImage_ShellBackground: row not found on this environment"]));
		_binder.RemoveBinding(default, default).ReturnsForAnyArgs([]);
		List<string> bound = [];
		List<string> warnings = [];

		// Act
		bool boundAll = _service.TryBindGalleryImage(_binder, GalleryImage, "ShellBackground", bound, warnings,
			out string error);

		// Assert
		boundAll.Should().BeTrue(because: $"a refused row is a warning, not a failed binding (error: {error})");
		bound.Should().BeEmpty(because: "a package must not ship a membership that names an image it does not carry");
		warnings.Should().HaveCount(2, because: "the caller must learn why the image and the membership are missing");
		_binder.DidNotReceive().BindRow("SysImageInTag", Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
			Arg.Any<Guid>());
		_binder.Received(1).RemoveBinding("SysImageInTag_ShellBackground", "SysImageInTag");
	}

	[Test]
	[Description("Reports the parts already bound when a later binding step fails, a request timeout included.")]
	public void TryBindGalleryImage_Should_Report_The_Parts_Bound_Before_A_Failure() {
		// Arrange
		_binder.BindRow("SysImageInTag", Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<Guid>())
			.Throws(new AggregateException("package is locked", new TaskCanceledException()));
		List<string> bound = [];

		// Act
		bool boundAll = _service.TryBindGalleryImage(_binder, GalleryImage, BindingSuffix, bound, [],
			out string error);

		// Assert
		boundAll.Should().BeFalse(because: "the membership binding threw");
		error.Should().Be("package is locked (A task was canceled.)",
			because: "a request timeout is a failed binding, and its reason reaches the caller");
		bound.Should().Equal([BrandingBackgroundService.ImagePart],
			because: "the parts that landed stay in the package, so the caller must learn which they are");
	}

	[Test]
	[Description("Lets a cancelled binding through instead of reporting it as a failed binding.")]
	public void TryBindGalleryImage_Should_Let_A_Cancellation_Through() {
		// Arrange
		_binder.BindRow(default, default, default, default).ThrowsForAnyArgs(new OperationCanceledException());

		// Act
		Action bind = () => _service.TryBindGalleryImage(_binder, GalleryImage, BindingSuffix, [], [], out _);

		// Assert
		bind.Should().Throw<OperationCanceledException>(
			because: "a cancelled call is the caller's decision, not a failed binding to report");
	}

	[Test]
	[Description("Selects the named package on the given binder and reports the resolved name.")]
	public void TryUsePackage_Should_Select_The_Package_On_The_Given_Binder() {
		// Arrange
		_binder.UsePackage("UsrThemes").Returns("UsrThemes");

		// Act
		bool selected = _service.TryUsePackage(_binder, "UsrThemes", out string package, out string error);

		// Assert
		selected.Should().BeTrue(because: $"the binder selected the package (error: {error})");
		package.Should().Be("UsrThemes", because: "the caller names the package the parts land in");
	}

	[Test]
	[Description("Turns a package that cannot be selected into a failure with the reason, and no package.")]
	public void TryUsePackage_Should_Report_A_Package_That_Cannot_Be_Selected() {
		// Arrange
		_binder.UsePackage(default).ThrowsForAnyArgs(new InvalidOperationException("package is locked"));

		// Act
		bool selected = _service.TryUsePackage(_binder, "UsrThemes", out string package, out string error);

		// Assert
		selected.Should().BeFalse(because: "the binder could not select the package");
		package.Should().BeNull(because: "nothing can be bound into a package that was not selected");
		error.Should().Be("package is locked", because: "the caller must learn why the package was not selected");
	}

	[Test]
	[Description("Binds the UsePanelIconBackground off state and records it as a bound part.")]
	public void TryBindPanelIconOffState_Should_Bind_The_Off_State() {
		// Arrange
		_binder.BindFeatureOffState("UsePanelIconBackground").Returns(PackageDataBindingOutcome.Success());
		List<string> bound = [];
		List<string> warnings = [];

		// Act
		bool completed = _service.TryBindPanelIconOffState(_binder, bound, warnings, out string error);

		// Assert
		completed.Should().BeTrue(because: $"the binding completed (error: {error})");
		bound.Should().Equal([BrandingBackgroundService.PanelIconOffStatePart],
			because: "the off state is the part this step delivers");
		warnings.Should().BeEmpty(because: "nothing was refused");
	}

	[Test]
	[Description("Reports a failed off-state binding with the reason instead of throwing.")]
	public void TryBindPanelIconOffState_Should_Report_A_Failed_Binding() {
		// Arrange
		_binder.BindFeatureOffState(default).ThrowsForAnyArgs(new InvalidOperationException("SaveSchema rejected"));
		List<string> bound = [];

		// Act
		bool completed = _service.TryBindPanelIconOffState(_binder, bound, [], out string error);

		// Assert
		completed.Should().BeFalse(because: "the binding did not complete");
		error.Should().Be("SaveSchema rejected", because: "the caller must learn why the binding failed");
		bound.Should().BeEmpty(because: "nothing was bound");
	}

	private void ArrangeImageLookup(string response) {
		_applicationClient.ExecuteGetRequest(Arg.Is<string>(url => url.StartsWith("odata/SysImage?"))).Returns(response);
	}

	private void ArrangeMembershipReads(string first, params string[] next) {
		_applicationClient.ExecuteGetRequest(Arg.Is<string>(url => url.StartsWith("odata/SysImageInTag?")))
			.Returns(first, next);
	}
}
