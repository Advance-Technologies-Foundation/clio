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
	[Description("Turns the panel icon background off for All employees after a background is written, unless the caller keeps it, and says which in the log and the outcome.")]
	public void Complete_Should_Turn_The_Panel_Icon_Off_Unless_Kept(bool keep, int expectedWrites, string expectedLine,
		string expectedState) {
		IFeatureStateService featureState = Substitute.For<IFeatureStateService>();
		ILogger logger = Substitute.For<ILogger>();

		ThemeBackgroundOutcome outcome = CreateWriter(featureState: featureState, logger: logger)
			.Complete(ColorBackground, keep, ThemeId, null);

		featureState.Received(expectedWrites).SetFeatureState("UsePanelIconBackground", SysAdminUnitIds.AllEmployees, false);
		logger.Received(1).WriteInfo(expectedLine);
		outcome.PanelIconBackground.Should().Be(expectedState);
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
		outcome.ImagePackage.Should().Be("UsrThemes");
	}

	[TestCase("{\"value\":[{\"Id\":\"6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81\"}]}", true)]
	[TestCase("{\"value\":[]}", false)]
	[Description("Confirms a background-image-id only when the environment has that SysImage row.")]
	public void TryConfirmImage_Should_Require_The_SysImage_Row(string odataResponse, bool expected) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecuteGetRequest(default).ReturnsForAnyArgs(odataResponse);

		bool confirmed = CreateWriter(applicationClient: applicationClient).TryConfirmImage(ImageBackground, out _);

		confirmed.Should().Be(expected, because: "only an image the environment has may become a theme background");
	}

	[Test]
	[Description("Names the missing image and says nothing was changed when the environment has no SysImage row for it.")]
	public void TryConfirmImage_Should_Name_The_Missing_Image() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecuteGetRequest(default).ReturnsForAnyArgs("{\"value\":[]}");

		CreateWriter(applicationClient: applicationClient).TryConfirmImage(ImageBackground, out string error);

		error.Should().Be($"No uploaded image with id '{ImageId}' was found in the environment, so nothing was changed. " +
			"Upload the file first with upload-image and pass the id it prints.",
			because: "the caller must learn that the image is missing and how to provide it");
	}

	[Test]
	[Description("Refuses the image without throwing when the SysImage query fails, so an unchecked id never reaches the theme write.")]
	public void TryConfirmImage_Should_Refuse_The_Image_When_The_Query_Throws() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecuteGetRequest(default).ThrowsForAnyArgs(new InvalidOperationException("connection reset"));

		bool confirmed = CreateWriter(applicationClient: applicationClient).TryConfirmImage(ImageBackground,
			out string error);

		confirmed.Should().BeFalse(because: "an image the environment could not confirm must stop the write");
		error.Should().Be($"Could not check image '{ImageId}' in the environment, so nothing was changed: connection reset",
			because: "the caller must learn that the check, not the image, failed");
	}

	[TestCase(null)]
	[TestCase("#123")]
	[TestCase("")]
	[Description("Confirms a color, a removal or no background without asking the environment for an image.")]
	public void TryConfirmImage_Should_Not_Query_Without_An_Image(string color) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		ThemeBackground background = color switch {
			null => null,
			"" => new ThemeBackground(),
			_ => ColorBackground
		};

		bool confirmed = CreateWriter(applicationClient: applicationClient).TryConfirmImage(background, out _);

		confirmed.Should().BeTrue(because: "only an image background names a row that must exist");
		applicationClient.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default);
	}

	[Test]
	[Description("Strips a credential from the server message it puts in the image check error, because the error reaches the log and the MCP transcript.")]
	public void TryConfirmImage_Should_Redact_A_Credential_In_The_Error() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecuteGetRequest(default).ThrowsForAnyArgs(new InvalidOperationException(MessageWithCredential));

		CreateWriter(applicationClient: applicationClient).TryConfirmImage(ImageBackground, out string error);

		error.Should().NotContain("hunter2", because: "a credential in a server message must not reach the caller");
	}

	[Test]
	[Description("Turns a failure to switch the panel icon background off into a warning and reports no panel icon state, because the theme is already written.")]
	public void Complete_Should_Warn_When_Turning_The_Panel_Icon_Off_Fails() {
		IFeatureStateService featureState = Substitute.For<IFeatureStateService>();
		featureState.WhenForAnyArgs(service => service.SetFeatureState(default, default, default, default))
			.Do(_ => throw new InvalidOperationException("feature service unavailable"));

		ThemeBackgroundOutcome outcome = CreateWriter(featureState: featureState).Complete(
			ColorBackground, false, ThemeId, null);

		outcome.Should().BeEquivalentTo(new ThemeBackgroundOutcome([
				"The theme background was applied, but turning off the UsePanelIconBackground feature failed, " +
				"so the panel may still hide it: feature service unavailable"
			]),
			because: "a failure after the theme write is a warning, and the panel icon state is unknown");
	}

	[Test]
	[Description("Turns a failure to open the theme's package into a warning and reports no image package, because the theme is already written.")]
	public void Complete_Should_Warn_When_Using_The_Theme_Package_Fails() {
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();
		binder.UsePackage(default).ThrowsForAnyArgs(new InvalidOperationException("package is locked"));

		ThemeBackgroundOutcome outcome = CreateWriter(binder: binder).Complete(ImageBackground, true, ThemeId, "UsrThemes");

		outcome.Should().Match<ThemeBackgroundOutcome>(value => value.ImagePackage == null
				&& value.Warnings.Count == 1
				&& value.Warnings[0].StartsWith(
					$"The theme background was applied, but binding image {ImageId} into the theme's package failed"),
			because: "a failed binding after the theme write is a warning, and no package holds the image");
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

	[Test]
	[Description("Strips a credential from the server messages it puts in its warnings, because the warnings reach the log and the MCP transcript.")]
	public void Complete_Should_Redact_A_Credential_In_The_Warnings() {
		IFeatureStateService featureState = Substitute.For<IFeatureStateService>();
		featureState.WhenForAnyArgs(service => service.SetFeatureState(default, default, default, default))
			.Do(_ => throw new InvalidOperationException(MessageWithCredential));
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();
		binder.UsePackage(default).ThrowsForAnyArgs(new InvalidOperationException(MessageWithCredential));

		ThemeBackgroundOutcome outcome = CreateWriter(featureState, binder).Complete(ImageBackground, false, ThemeId,
			"UsrThemes");

		outcome.Warnings.Should().HaveCount(2).And.NotContain(warning => warning.Contains("hunter2"),
			because: "a credential in a server message must not reach the caller");
	}

	[TestCase(true)]
	[TestCase(false)]
	[Description("Leaves the panel icon feature and the theme's package alone when the background is removed or not given.")]
	public void Complete_Should_Not_Touch_The_Feature_Or_The_Package_Without_A_Background_To_Set(bool removed) {
		IFeatureStateService featureState = Substitute.For<IFeatureStateService>();
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();

		ThemeBackgroundOutcome outcome = CreateWriter(featureState, binder).Complete(
			removed ? new ThemeBackground() : null, false, ThemeId, "UsrThemes");

		featureState.DidNotReceiveWithAnyArgs().SetFeatureState(default, default, default, default);
		binder.DidNotReceiveWithAnyArgs().UsePackage(default);
		binder.DidNotReceiveWithAnyArgs().BindRow(default, default, default, default);
		outcome.Should().BeEquivalentTo(ThemeBackgroundOutcome.None,
			because: "a removal neither switches the feature nor binds anything");
	}

	private static ThemeBackgroundWriter CreateWriter(IFeatureStateService featureState = null,
		IPackageDataBinder binder = null, IApplicationClient applicationClient = null, ILogger logger = null) {
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(call => call.Arg<string>());
		return new ThemeBackgroundWriter(
			new BrandingBackgroundService(applicationClient ?? Substitute.For<IApplicationClient>(), urlBuilder,
				featureState ?? Substitute.For<IFeatureStateService>()),
			binder ?? Substitute.For<IPackageDataBinder>(), logger ?? Substitute.For<ILogger>());
	}
}
