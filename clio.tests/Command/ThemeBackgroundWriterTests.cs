using System;
using Clio.Command;
using Clio.Command.Branding;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ThemeBackgroundWriterTests {

	private const string ImageId = "6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81";

	private const string ThemeId = "d53a816b-65ed-4b5b-ad3b-f739280add45";

	private const string BindingSuffix = "ThemeBackground_d53a816b65ed4b5bad3bf739280add45";

	private const string MessageWithCredential = "refused by http://admin:hunter2@proxy.internal.example:8080/trace";

	private static readonly ThemeBackground ColorBackground = new() { Mode = ThemeBackground.ColorMode, Color = "#123" };

	private static readonly ThemeBackground ImageBackground = new() { Mode = ThemeBackground.ImageMode, ImageId = ImageId };

	private static readonly BackgroundGalleryImage GalleryImage = new(Guid.Parse(ImageId),
		Guid.Parse("8b1c2d3e-4f50-4a6b-9c7d-e8f9a0b1c2d3"), BrandingBackgroundService.ShellBackgroundTagId);

	[Test]
	[Description("Adds the image of an image background to the background gallery and hands the gallery entry back.")]
	public void TryAddImageToBackgroundGallery_Should_Add_The_Background_Image() {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.TryConfirmUploadedImage(Guid.Parse(ImageId), out Arg.Any<string>()).Returns(true);
		service.TryAddImageToBackgroundGallery(Guid.Parse(ImageId), out Arg.Any<BackgroundGalleryImage>(),
				out Arg.Any<string>())
			.Returns(call => {
				call[1] = GalleryImage;
				return true;
			});

		// Act
		bool added = CreateWriter(service).TryAddImageToBackgroundGallery(ImageBackground,
			out BackgroundGalleryImage image, out string error);

		// Assert
		added.Should().BeTrue(because: $"an image the gallery accepted may become the theme background (error: {error})");
		image.Should().Be(GalleryImage, because: "the binding after the theme write needs this gallery entry");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Refuses the theme write when the uploaded image cannot be confirmed or cannot join the gallery, says the theme was not written, and strips a credential from the reason.")]
	public void TryAddImageToBackgroundGallery_Should_Refuse_The_Theme_Write_When_The_Image_Is_Refused(
		bool refusedByCheck) {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.TryConfirmUploadedImage(default, out _).ReturnsForAnyArgs(call => {
			call[1] = refusedByCheck ? MessageWithCredential : null;
			return !refusedByCheck;
		});
		service.TryAddImageToBackgroundGallery(default, out _, out _).ReturnsForAnyArgs(call => {
			call[2] = MessageWithCredential;
			return false;
		});

		// Act
		bool added = CreateWriter(service).TryAddImageToBackgroundGallery(ImageBackground, out _, out string error);

		// Assert
		added.Should().BeFalse(because: "a theme background must name an image the gallery holds");
		error.Should().StartWith("refused by ").And.EndWith(" The theme was not written.")
			.And.NotContain("hunter2", because: "a credential in a server message must not reach the caller");
	}

	[TestCase(null)]
	[TestCase("#123")]
	[TestCase("")]
	[Description("Lets a color, a removal or no background through without asking the environment for an image.")]
	public void TryAddImageToBackgroundGallery_Should_Not_Query_Without_An_Image(string color) {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		ThemeBackground background = color switch {
			null => null,
			"" => new ThemeBackground(),
			_ => ColorBackground
		};

		// Act
		bool added = CreateWriter(service).TryAddImageToBackgroundGallery(background, out BackgroundGalleryImage image,
			out _);

		// Assert
		added.Should().BeTrue(because: "only an image background names a row that must join the gallery");
		image.Should().BeNull(because: "there is no image to bind after the theme write");
		service.ReceivedCalls().Should().BeEmpty(because: "nothing about the gallery needs the environment here");
	}

	[TestCase(false, 1, "UsePanelIconBackground is off for All employees.", ThemeBackgroundOutcome.PanelIconOff)]
	[TestCase(true, 0, "UsePanelIconBackground was left as it is (keep-icon-background).", ThemeBackgroundOutcome.PanelIconKept)]
	[Description("Turns the panel icon background off after a background is written, unless the caller keeps it, and says which in the log and the outcome.")]
	public void TurnOffPanelIconBackground_Should_Turn_The_Panel_Icon_Off_Unless_Kept(bool keep, int expectedTurnOffs,
		string expectedLine, string expectedState) {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.TryTurnOffPanelIconBackground(default, out _).ReturnsForAnyArgs(true);
		ILogger logger = Substitute.For<ILogger>();

		// Act
		PanelIconBackgroundOutcome outcome = CreateWriter(service, logger).TurnOffPanelIconBackground(ColorBackground, keep);

		// Assert
		service.Received(expectedTurnOffs).TryTurnOffPanelIconBackground("The theme background", out Arg.Any<string>());
		logger.Received(1).WriteInfo(expectedLine);
		outcome.Should().BeEquivalentTo(new PanelIconBackgroundOutcome(expectedState, []),
			because: "an MCP client reads the panel icon state only from the create-theme result");
	}

	[Test]
	[Description("Turns a failed panel icon turn-off into a warning without a credential and reports no panel icon state, because the theme is already written.")]
	public void TurnOffPanelIconBackground_Should_Warn_When_Turning_The_Panel_Icon_Off_Fails() {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.TryTurnOffPanelIconBackground(default, out _).ReturnsForAnyArgs(call => {
			call[1] = MessageWithCredential;
			return false;
		});

		// Act
		PanelIconBackgroundOutcome outcome = CreateWriter(service).TurnOffPanelIconBackground(ColorBackground, false);

		// Assert
		outcome.Should().Match<PanelIconBackgroundOutcome>(value => value.State == null
				&& value.Warnings.Count == 1
				&& value.Warnings[0].StartsWith("refused by ")
				&& !value.Warnings[0].Contains("hunter2"),
			because: "a failure after the theme write is a warning without a credential, and the panel icon state is unknown");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Leaves the panel icon feature alone when the background is removed or not given.")]
	public void TurnOffPanelIconBackground_Should_Not_Touch_The_Feature_Without_A_Background_To_Set(bool removed) {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();

		// Act
		PanelIconBackgroundOutcome outcome = CreateWriter(service)
			.TurnOffPanelIconBackground(removed ? new ThemeBackground() : null, false);

		// Assert
		service.ReceivedCalls().Should().BeEmpty(because: "a removal does not switch the feature");
		outcome.Should().BeEquivalentTo(PanelIconBackgroundOutcome.None, because: "nothing happened after the theme write");
	}

	[Test]
	[Description("Binds the image and its gallery membership into the theme's package through the writer's binder under the theme's binding name, and names the package in the log and the outcome.")]
	public void BindImageIntoThemePackage_Should_Bind_The_Image_Into_The_Theme_Package() {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.BindGalleryImage(default, default, default).ReturnsForAnyArgs(new GalleryImageBindingOutcome(true,
			[BrandingBackgroundService.ImagePart, BrandingBackgroundService.GalleryMembershipPart], []));
		IPackageDataBinder binder = ThemePackageBinder();
		ILogger logger = Substitute.For<ILogger>();

		// Act
		ThemeImageBindingOutcome outcome = CreateWriter(service, logger, binder)
			.BindImageIntoThemePackage(GalleryImage, ThemeId, "UsrThemes");

		// Assert
		Received.InOrder(() => {
			binder.UsePackage("UsrThemes");
			service.BindGalleryImage(binder, GalleryImage, BindingSuffix);
		});
		logger.Received(1).WriteInfo("Bound the background into package 'UsrThemes': image, gallery-membership.");
		outcome.Should().BeEquivalentTo(new ThemeImageBindingOutcome("UsrThemes", []),
			because: "the caller must learn which package now ships the image");
	}

	[Test]
	[Description("Turns a theme package that cannot be selected into a warning without a credential, and binds nothing, because the theme is already written.")]
	public void BindImageIntoThemePackage_Should_Warn_When_The_Theme_Package_Cannot_Be_Selected() {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();
		binder.UsePackage(default).ThrowsForAnyArgs(new InvalidOperationException(MessageWithCredential));

		// Act
		ThemeImageBindingOutcome outcome = CreateWriter(service, binder: binder)
			.BindImageIntoThemePackage(GalleryImage, ThemeId, "UsrThemes");

		// Assert
		outcome.Should().Match<ThemeImageBindingOutcome>(value => value.Package == null
				&& value.Warnings.Count == 1
				&& value.Warnings[0].StartsWith(
					"The theme background was applied, but binding it into the theme's package failed: refused by ")
				&& !value.Warnings[0].Contains("hunter2"),
			because: "a package that cannot be selected holds nothing, and the reason reaches the caller without a credential");
		service.DidNotReceiveWithAnyArgs().BindGalleryImage(default, default, default);
	}

	[Test]
	[Description("Passes on the binding warnings without a credential and reports no image package when the image is not bound.")]
	public void BindImageIntoThemePackage_Should_Pass_On_The_Binding_Warnings_When_The_Image_Is_Not_Bound() {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.BindGalleryImage(default, default, default).ReturnsForAnyArgs(
			new GalleryImageBindingOutcome(false, [], [MessageWithCredential]));

		// Act
		ThemeImageBindingOutcome outcome = CreateWriter(service, binder: ThemePackageBinder())
			.BindImageIntoThemePackage(GalleryImage, ThemeId, "UsrThemes");

		// Assert
		outcome.Should().Match<ThemeImageBindingOutcome>(value => value.Package == null
				&& value.Warnings.Count == 1
				&& value.Warnings[0].StartsWith("refused by ")
				&& !value.Warnings[0].Contains("hunter2"),
			because: "the caller must learn why the image is not in the package, without a credential");
	}

	[Test]
	[Description("Turns a binding that stopped before the image into a warning without a credential that names the package, and reports no image package, because the theme is already written.")]
	public void BindImageIntoThemePackage_Should_Warn_When_The_Binding_Stops() {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.BindGalleryImage(default, default, default).ReturnsForAnyArgs(
			new GalleryImageBindingOutcome(false, [], [], MessageWithCredential));

		// Act
		ThemeImageBindingOutcome outcome = CreateWriter(service, binder: ThemePackageBinder())
			.BindImageIntoThemePackage(GalleryImage, ThemeId, "UsrThemes");

		// Assert
		outcome.Should().Match<ThemeImageBindingOutcome>(value => value.Package == null
				&& value.Warnings.Count == 1
				&& value.Warnings[0].StartsWith(
					"The theme background was applied, but binding it into package 'UsrThemes' failed: refused by ")
				&& !value.Warnings[0].Contains("hunter2"),
			because: "a failed binding after the theme write is a warning without a credential, and no package holds the image");
	}

	[Test]
	[Description("Names the package and the bound image when the binding stops after the image, so the warning agrees with the reported image package.")]
	public void BindImageIntoThemePackage_Should_Name_What_Was_Bound_When_The_Binding_Stops_After_The_Image() {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.BindGalleryImage(default, default, default).ReturnsForAnyArgs(new GalleryImageBindingOutcome(true,
			[BrandingBackgroundService.ImagePart], [], "package is locked"));

		// Act
		ThemeImageBindingOutcome outcome = CreateWriter(service, binder: ThemePackageBinder())
			.BindImageIntoThemePackage(GalleryImage, ThemeId, "UsrThemes");

		// Assert
		outcome.Should().BeEquivalentTo(new ThemeImageBindingOutcome("UsrThemes", [
				"The theme background was applied, but binding it into package 'UsrThemes' failed: package is locked " +
				"Already bound and left in place: image."
			]),
			because: "the image is in the package, so the warning must not claim the image binding failed");
	}

	[Test]
	[Description("Binds nothing for a background without an image.")]
	public void BindImageIntoThemePackage_Should_Not_Bind_Without_An_Image() {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();

		// Act
		ThemeImageBindingOutcome outcome = CreateWriter(service).BindImageIntoThemePackage(null, ThemeId, "UsrThemes");

		// Assert
		service.ReceivedCalls().Should().BeEmpty(because: "a color or a removal names no image to bind");
		outcome.Should().BeEquivalentTo(ThemeImageBindingOutcome.None, because: "nothing was bound");
	}

	[Test]
	[Description("Combines the panel icon and the binding outcomes into the create-theme background outcome, panel icon warnings first.")]
	public void ThemeBackgroundOutcome_From_Should_Combine_Both_Outcomes() {
		// Act
		ThemeBackgroundOutcome outcome = ThemeBackgroundOutcome.From(
			new PanelIconBackgroundOutcome(null, ["panel icon failed"]),
			new ThemeImageBindingOutcome("UsrThemes", ["membership refused"]));

		// Assert
		outcome.Should().BeEquivalentTo(
			new ThemeBackgroundOutcome(["panel icon failed", "membership refused"], "UsrThemes"),
			because: "the warnings keep the order the steps ran in, and the package and state come from their steps");
	}

	private static IPackageDataBinder ThemePackageBinder() {
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();
		binder.UsePackage("UsrThemes").Returns("UsrThemes");
		return binder;
	}

	private static ThemeBackgroundWriter CreateWriter(IBrandingBackgroundService service = null, ILogger logger = null,
		IPackageDataBinder binder = null) {
		return new ThemeBackgroundWriter(service ?? Substitute.For<IBrandingBackgroundService>(),
			binder ?? Substitute.For<IPackageDataBinder>(), logger ?? Substitute.For<ILogger>());
	}
}
