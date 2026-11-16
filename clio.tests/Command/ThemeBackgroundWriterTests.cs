using System;
using System.Collections.Generic;
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
		bool added = CreateWriter(service).TryAddImageToBackgroundGallery(ImageBackground, "UsrThemes",
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
		bool added = CreateWriter(service).TryAddImageToBackgroundGallery(ImageBackground, "UsrThemes", out _,
			out string error);

		// Assert
		added.Should().BeFalse(because: "a theme background must name an image the gallery holds");
		error.Should().StartWith("refused by ").And.EndWith(" The theme was not written.")
			.And.NotContain("hunter2", because: "a credential in a server message must not reach the caller");
	}

	[Test]
	[Description("Refuses the theme write before the image is checked or joins the gallery when the theme's package cannot be resolved, and strips a credential from the reason.")]
	public void TryAddImageToBackgroundGallery_Should_Refuse_The_Theme_Write_When_The_Package_Cannot_Be_Resolved() {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		IPackageTargetResolver resolver = Substitute.For<IPackageTargetResolver>();
		resolver.Resolve("UsrThemes", true).Returns(PackageTargetResolution.Unresolvable(MessageWithCredential));

		// Act
		bool added = CreateWriter(service, resolver: resolver).TryAddImageToBackgroundGallery(ImageBackground,
			"UsrThemes", out BackgroundGalleryImage image, out string error);

		// Assert
		added.Should().BeFalse(because: "a package the background cannot land in refuses the theme write, so nothing may change before it");
		image.Should().BeNull(because: "no image joined the gallery");
		error.Should().StartWith("refused by ").And.EndWith(" The theme was not written.")
			.And.NotContain("hunter2", because: "a credential in a server message must not reach the caller");
		service.ReceivedCalls().Should().BeEmpty(
			because: "the image is neither checked nor added to the environment-wide gallery for a theme that cannot be written");
	}

	[Test]
	[Description("Names the locked package and nothing else when the theme's package is locked, so the caller is not told to unlock it, and leaves the gallery alone.")]
	public void TryAddImageToBackgroundGallery_Should_State_Only_The_Lock_When_The_Package_Is_Locked() {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		IPackageTargetResolver resolver = Substitute.For<IPackageTargetResolver>();
		resolver.Resolve("UsrThemes", true).Returns(PackageTargetResolution.Locked("UsrThemes",
			"Package 'UsrThemes' is locked, so it cannot receive design-time writes. Unlock it with unlock-package."));

		// Act
		bool added = CreateWriter(service, resolver: resolver).TryAddImageToBackgroundGallery(ImageBackground,
			"UsrThemes", out _, out string error);

		// Assert
		added.Should().BeFalse(because: "ThemeService cannot write a theme into a locked package");
		error.Should().Be("The theme's package 'UsrThemes' is locked. The theme was not written.",
			because: "an agent acts on advice in an error, and unlocking a package is the user's decision");
		service.ReceivedCalls().Should().BeEmpty(because: "the image does not join the gallery for a theme that cannot be written");
	}

	[TestCase(null)]
	[TestCase("#123")]
	[TestCase("")]
	[Description("Lets a color, a removal or no background through without asking the environment for an image or the package.")]
	public void TryAddImageToBackgroundGallery_Should_Not_Query_Without_An_Image(string color) {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		ThemeBackground background = color switch {
			null => null,
			"" => new ThemeBackground(),
			_ => ColorBackground
		};

		IPackageTargetResolver resolver = Substitute.For<IPackageTargetResolver>();

		// Act
		bool added = CreateWriter(service, resolver: resolver).TryAddImageToBackgroundGallery(background, "UsrThemes",
			out BackgroundGalleryImage image, out _);

		// Assert
		added.Should().BeTrue(because: "only an image background names a row that must join the gallery");
		image.Should().BeNull(because: "there is no image to bind after the theme write");
		service.ReceivedCalls().Should().BeEmpty(because: "nothing about the gallery needs the environment here");
		resolver.ReceivedCalls().Should().BeEmpty(because: "only the gallery step needs the package checked first");
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
	[Description("Selects the theme's package, then binds the image, its gallery membership and the panel icon off state through the writer's binder, and names the package in the log and the outcome.")]
	public void BindBackgroundIntoThemePackage_Should_Bind_An_Image_Background_Into_The_Theme_Package() {
		// Arrange
		IBrandingBackgroundService service = ServiceWithThemePackage();
		StubGalleryImageBinding(service,
			[BrandingBackgroundService.ImagePart, BrandingBackgroundService.GalleryMembershipPart], []);
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();
		ILogger logger = Substitute.For<ILogger>();

		// Act
		ThemeBackgroundBindingOutcome outcome = CreateWriter(service, logger, binder)
			.BindBackgroundIntoThemePackage(ImageBackground, GalleryImage, ThemeId, "UsrThemes");

		// Assert
		Received.InOrder(() => {
			service.TryUsePackage(binder, "UsrThemes", out Arg.Any<string>(), out Arg.Any<string>());
			service.TryBindGalleryImage(binder, GalleryImage, BindingSuffix, Arg.Any<List<string>>(),
				Arg.Any<List<string>>(), out Arg.Any<string>());
			service.TryBindPanelIconOffState(binder, Arg.Any<List<string>>(), Arg.Any<List<string>>(),
				out Arg.Any<string>());
		});
		logger.Received(1).WriteInfo(
			"Bound the background into package 'UsrThemes': image, gallery-membership, panel-icon-off-state.");
		outcome.Should().BeEquivalentTo(new ThemeBackgroundBindingOutcome("UsrThemes", []),
			because: "the caller must learn which package now ships the image");
	}

	[Test]
	[Description("Binds only the panel icon off state for a color background, which ships with every background, and reports no image package.")]
	public void BindBackgroundIntoThemePackage_Should_Bind_The_Panel_Icon_Off_State_For_A_Color_Background() {
		// Arrange
		IBrandingBackgroundService service = ServiceWithThemePackage();
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();

		// Act
		ThemeBackgroundBindingOutcome outcome = CreateWriter(service, binder: binder)
			.BindBackgroundIntoThemePackage(ColorBackground, null, ThemeId, "UsrThemes");

		// Assert
		service.Received(1).TryBindPanelIconOffState(binder, Arg.Any<List<string>>(), Arg.Any<List<string>>(),
			out Arg.Any<string>());
		service.DidNotReceiveWithAnyArgs().TryBindGalleryImage(default, default, default, default, default, out _);
		outcome.Should().BeEquivalentTo(new ThemeBackgroundBindingOutcome(null, []),
			because: "a color names no image, so no package holds one");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Binds nothing when the background is removed or not given.")]
	public void BindBackgroundIntoThemePackage_Should_Not_Bind_Without_A_Background_To_Set(bool removed) {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();

		// Act
		ThemeBackgroundBindingOutcome outcome = CreateWriter(service)
			.BindBackgroundIntoThemePackage(removed ? new ThemeBackground() : null, null, ThemeId, "UsrThemes");

		// Assert
		service.ReceivedCalls().Should().BeEmpty(because: "a removal delivers nothing into the package");
		outcome.Should().BeEquivalentTo(ThemeBackgroundBindingOutcome.None, because: "nothing was bound");
	}

	[Test]
	[Description("Turns a theme package that cannot be selected into a warning without a credential, and binds nothing, because the theme is already written.")]
	public void BindBackgroundIntoThemePackage_Should_Warn_When_The_Theme_Package_Cannot_Be_Selected() {
		// Arrange
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.TryUsePackage(default, default, out _, out _).ReturnsForAnyArgs(call => {
			call[3] = MessageWithCredential;
			return false;
		});

		// Act
		ThemeBackgroundBindingOutcome outcome = CreateWriter(service)
			.BindBackgroundIntoThemePackage(ImageBackground, GalleryImage, ThemeId, "UsrThemes");

		// Assert
		outcome.Should().Match<ThemeBackgroundBindingOutcome>(value => value.ImagePackage == null
				&& value.Warnings.Count == 1
				&& value.Warnings[0].StartsWith(
					"The theme background was applied, but binding it into the theme's package failed: refused by ")
				&& !value.Warnings[0].Contains("hunter2"),
			because: "a package that cannot be selected holds nothing, and the reason reaches the caller without a credential");
		service.DidNotReceiveWithAnyArgs().TryBindGalleryImage(default, default, default, default, default, out _);
		service.DidNotReceiveWithAnyArgs().TryBindPanelIconOffState(default, default, default, out _);
	}

	[Test]
	[Description("Passes on the binding warnings without a credential and reports no image package when the image is not bound.")]
	public void BindBackgroundIntoThemePackage_Should_Pass_On_The_Binding_Warnings_When_The_Image_Is_Not_Bound() {
		// Arrange
		IBrandingBackgroundService service = ServiceWithThemePackage();
		StubGalleryImageBinding(service, [], [MessageWithCredential]);

		// Act
		ThemeBackgroundBindingOutcome outcome = CreateWriter(service)
			.BindBackgroundIntoThemePackage(ImageBackground, GalleryImage, ThemeId, "UsrThemes");

		// Assert
		outcome.Should().Match<ThemeBackgroundBindingOutcome>(value => value.ImagePackage == null
				&& value.Warnings.Count == 1
				&& value.Warnings[0].StartsWith("refused by ")
				&& !value.Warnings[0].Contains("hunter2"),
			because: "the caller must learn why the image is not in the package, without a credential");
	}

	[Test]
	[Description("Names the package and the bound image when the binding stops after the image, binds nothing more, and keeps the image package, so the warning agrees with it.")]
	public void BindBackgroundIntoThemePackage_Should_Name_What_Was_Bound_When_The_Binding_Stops_After_The_Image() {
		// Arrange
		IBrandingBackgroundService service = ServiceWithThemePackage();
		StubGalleryImageBinding(service, [BrandingBackgroundService.ImagePart], [], MessageWithCredential);

		// Act
		ThemeBackgroundBindingOutcome outcome = CreateWriter(service)
			.BindBackgroundIntoThemePackage(ImageBackground, GalleryImage, ThemeId, "UsrThemes");

		// Assert
		outcome.Should().Match<ThemeBackgroundBindingOutcome>(value => value.ImagePackage == "UsrThemes"
				&& value.Warnings.Count == 1
				&& value.Warnings[0].StartsWith(
					"The theme background was applied, but binding it into package 'UsrThemes' failed: refused by ")
				&& value.Warnings[0].EndsWith(" Already bound and left in place: image.")
				&& !value.Warnings[0].Contains("hunter2"),
			because: "the image is in the package, so the warning must not claim the image binding failed");
		service.DidNotReceiveWithAnyArgs().TryBindPanelIconOffState(default, default, default, out _);
	}

	[Test]
	[Description("Combines the panel icon and the binding outcomes into the create-theme background outcome, panel icon warnings first.")]
	public void ThemeBackgroundOutcome_From_Should_Combine_Both_Outcomes() {
		// Act
		ThemeBackgroundOutcome outcome = ThemeBackgroundOutcome.From(
			new PanelIconBackgroundOutcome(null, ["panel icon failed"]),
			new ThemeBackgroundBindingOutcome("UsrThemes", ["membership refused"]));

		// Assert
		outcome.Should().BeEquivalentTo(
			new ThemeBackgroundOutcome(["panel icon failed", "membership refused"], "UsrThemes"),
			because: "the warnings keep the order the steps ran in, and the package and state come from their steps");
	}

	private static IBrandingBackgroundService ServiceWithThemePackage() {
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.TryUsePackage(default, default, out _, out _).ReturnsForAnyArgs(call => {
			call[2] = "UsrThemes";
			return true;
		});
		service.TryBindPanelIconOffState(default, default, default, out _).ReturnsForAnyArgs(call => {
			((List<string>)call[1]).Add(BrandingBackgroundService.PanelIconOffStatePart);
			return true;
		});
		return service;
	}

	private static void StubGalleryImageBinding(IBrandingBackgroundService service, IReadOnlyList<string> bound,
		IReadOnlyList<string> warnings, string error = null) {
		service.TryBindGalleryImage(default, default, default, default, default, out _).ReturnsForAnyArgs(call => {
			((List<string>)call[3]).AddRange(bound);
			((List<string>)call[4]).AddRange(warnings);
			call[5] = error;
			return error is null;
		});
	}

	private static ThemeBackgroundWriter CreateWriter(IBrandingBackgroundService service = null, ILogger logger = null,
		IPackageDataBinder binder = null, IPackageTargetResolver resolver = null) {
		return new ThemeBackgroundWriter(service ?? Substitute.For<IBrandingBackgroundService>(),
			binder ?? Substitute.For<IPackageDataBinder>(), resolver ?? EditablePackageResolver(),
			logger ?? Substitute.For<ILogger>());
	}

	private static IPackageTargetResolver EditablePackageResolver() {
		IPackageTargetResolver resolver = Substitute.For<IPackageTargetResolver>();
		resolver.Resolve(default, default).ReturnsForAnyArgs(call =>
			PackageTargetResolution.Resolved((string)call[0] ?? "Custom", Guid.NewGuid()));
		return resolver;
	}
}
