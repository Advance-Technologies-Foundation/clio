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

	private const string MessageWithCredential = "refused by http://admin:hunter2@proxy.internal.example:8080/trace";

	private static readonly ThemeBackground ColorBackground = new() { Mode = ThemeBackground.ColorMode, Color = "#123" };

	private static readonly ThemeBackground ImageBackground = new() { Mode = ThemeBackground.ImageMode, ImageId = ImageId };

	[TestCase(false, 1, "UsePanelIconBackground is off for All employees.", ThemeBackgroundOutcome.PanelIconOff)]
	[TestCase(true, 0, "UsePanelIconBackground was left as it is (keep-icon-background).", ThemeBackgroundOutcome.PanelIconKept)]
	[Description("Turns the panel icon background off after a background is written, unless the caller keeps it, and says which in the log and the outcome.")]
	public void Complete_Should_Turn_The_Panel_Icon_Off_Unless_Kept(bool keep, int expectedTurnOffs, string expectedLine,
		string expectedState) {
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.TryTurnOffPanelIconBackground(default, out _).ReturnsForAnyArgs(true);
		ILogger logger = Substitute.For<ILogger>();

		ThemeBackgroundOutcome outcome = CreateWriter(service, logger: logger).Complete(ColorBackground, keep, ThemeId, null);

		service.Received(expectedTurnOffs).TryTurnOffPanelIconBackground("The theme background", out Arg.Any<string>());
		logger.Received(1).WriteInfo(expectedLine);
		outcome.PanelIconBackground.Should().Be(expectedState,
			because: "an MCP client reads the panel icon state only from the create-theme result");
	}

	[Test]
	[Description("Names the package the background image was bound into, in the log and the outcome.")]
	public void Complete_Should_Name_The_Package_It_Binds_The_Image_Into() {
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();
		binder.UsePackage("UsrThemes").Returns("UsrThemes");
		binder.BindRow(default, default, default, default).ReturnsForAnyArgs(PackageDataBindingOutcome.Success());
		ILogger logger = Substitute.For<ILogger>();

		ThemeBackgroundOutcome outcome = CreateWriter(binder: binder, logger: logger)
			.Complete(ImageBackground, true, ThemeId, "UsrThemes");

		logger.Received(1).WriteInfo("Bound the background image into package 'UsrThemes'.");
		outcome.ImagePackage.Should().Be("UsrThemes",
			because: "the caller must learn which package now ships the image");
	}

	[TestCase(true, true, null)]
	[TestCase(false, false, "No uploaded image with id '" + ImageId + "' was found in the environment, so nothing was " +
		"changed. Upload the file first with upload-image and pass the id it prints.")]
	[Description("Confirms a background-image-id only when the environment has that uploaded image, and otherwise names the missing image and says nothing was changed.")]
	public void TryConfirmImage_Should_Require_The_Uploaded_Image(bool exists, bool expected, string expectedError) {
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.TryFindUploadedImage(Guid.Parse(ImageId), out Arg.Any<bool>(), out Arg.Any<string>())
			.Returns(call => {
				call[1] = exists;
				return true;
			});

		bool confirmed = CreateWriter(service).TryConfirmImage(ImageBackground, out string error);

		confirmed.Should().Be(expected, because: "only an image the environment has may become a theme background");
		error.Should().Be(expectedError, because: "the caller must learn that the image is missing and how to provide it");
	}

	[Test]
	[Description("Refuses the image when the check itself fails, says that the check failed, and strips a credential from the reason, because the error reaches the log and the MCP transcript.")]
	public void TryConfirmImage_Should_Refuse_The_Image_When_The_Check_Fails() {
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.TryFindUploadedImage(default, out _, out _).ReturnsForAnyArgs(call => {
			call[2] = MessageWithCredential;
			return false;
		});

		bool confirmed = CreateWriter(service).TryConfirmImage(ImageBackground, out string error);

		confirmed.Should().BeFalse(because: "an image the environment could not confirm must stop the write");
		error.Should().StartWith($"Could not check image '{ImageId}' in the environment, so nothing was changed: ")
			.And.NotContain("hunter2", because: "a credential in a server message must not reach the caller");
	}

	[TestCase(null)]
	[TestCase("#123")]
	[TestCase("")]
	[Description("Confirms a color, a removal or no background without asking the environment for an image.")]
	public void TryConfirmImage_Should_Not_Query_Without_An_Image(string color) {
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		ThemeBackground background = color switch {
			null => null,
			"" => new ThemeBackground(),
			_ => ColorBackground
		};

		bool confirmed = CreateWriter(service).TryConfirmImage(background, out _);

		confirmed.Should().BeTrue(because: "only an image background names a row that must exist");
		service.DidNotReceiveWithAnyArgs().TryFindUploadedImage(default, out _, out _);
	}

	[Test]
	[Description("Turns a failed panel icon turn-off into a warning without a credential and reports no panel icon state, because the theme is already written.")]
	public void Complete_Should_Warn_When_Turning_The_Panel_Icon_Off_Fails() {
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		service.TryTurnOffPanelIconBackground(default, out _).ReturnsForAnyArgs(call => {
			call[1] = MessageWithCredential;
			return false;
		});

		ThemeBackgroundOutcome outcome = CreateWriter(service).Complete(ColorBackground, false, ThemeId, null);

		outcome.Should().Match<ThemeBackgroundOutcome>(value => value.PanelIconBackground == null
				&& value.Warnings.Count == 1
				&& value.Warnings[0].StartsWith("refused by ")
				&& !value.Warnings[0].Contains("hunter2"),
			because: "a failure after the theme write is a warning without a credential, and the panel icon state is unknown");
	}

	[Test]
	[Description("Turns a failure to open the theme's package into a warning without a credential and reports no image package, because the theme is already written.")]
	public void Complete_Should_Warn_When_Using_The_Theme_Package_Fails() {
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();
		binder.UsePackage(default).ThrowsForAnyArgs(new InvalidOperationException(MessageWithCredential));

		ThemeBackgroundOutcome outcome = CreateWriter(binder: binder).Complete(ImageBackground, true, ThemeId, "UsrThemes");

		outcome.Should().Match<ThemeBackgroundOutcome>(value => value.ImagePackage == null
				&& value.Warnings.Count == 1
				&& value.Warnings[0].StartsWith(
					$"The theme background was applied, but binding image {ImageId} into the theme's package failed")
				&& !value.Warnings[0].Contains("hunter2"),
			because: "a failed binding after the theme write is a warning without a credential, and no package holds the image");
	}

	[Test]
	[Description("Passes on the binder's warnings and reports no image package when the binder does not bind the image.")]
	public void Complete_Should_Pass_On_The_Binder_Warnings_When_The_Image_Is_Not_Bound() {
		const string binderWarning = "SysImage_ThemeBackground: row '6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81' not found on this environment";
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();
		binder.UsePackage("UsrThemes").Returns("UsrThemes");
		binder.BindRow(default, default, default, default)
			.ReturnsForAnyArgs(PackageDataBindingOutcome.Refused([binderWarning]));

		ThemeBackgroundOutcome outcome = CreateWriter(binder: binder).Complete(ImageBackground, true, ThemeId, "UsrThemes");

		outcome.Should().BeEquivalentTo(
			new ThemeBackgroundOutcome([binderWarning], null, ThemeBackgroundOutcome.PanelIconKept),
			because: "the caller must learn why the image is not in the package");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Leaves the panel icon feature and the theme's package alone when the background is removed or not given.")]
	public void Complete_Should_Not_Touch_The_Feature_Or_The_Package_Without_A_Background_To_Set(bool removed) {
		IBrandingBackgroundService service = Substitute.For<IBrandingBackgroundService>();
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();

		ThemeBackgroundOutcome outcome = CreateWriter(service, binder).Complete(
			removed ? new ThemeBackground() : null, false, ThemeId, "UsrThemes");

		service.DidNotReceiveWithAnyArgs().TryTurnOffPanelIconBackground(default, out _);
		binder.DidNotReceiveWithAnyArgs().UsePackage(default);
		binder.DidNotReceiveWithAnyArgs().BindRow(default, default, default, default);
		outcome.Should().BeEquivalentTo(ThemeBackgroundOutcome.None,
			because: "a removal neither switches the feature nor binds anything");
	}

	private static ThemeBackgroundWriter CreateWriter(IBrandingBackgroundService service = null,
		IPackageDataBinder binder = null, ILogger logger = null) {
		return new ThemeBackgroundWriter(service ?? Substitute.For<IBrandingBackgroundService>(),
			binder ?? Substitute.For<IPackageDataBinder>(), logger ?? Substitute.For<ILogger>());
	}
}
