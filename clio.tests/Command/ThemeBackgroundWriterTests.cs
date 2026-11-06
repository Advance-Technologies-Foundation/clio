using System;
using System.Text.Json;
using Clio.Command;
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

	[TestCase(false, 1, "UsePanelIconBackground is off for All employees.", ThemeBackgroundOutcome.PanelIconOff)]
	[TestCase(true, 0, "UsePanelIconBackground was left as it is (keep-icon-background).", ThemeBackgroundOutcome.PanelIconKept)]
	[Description("Turns the panel icon background off for All employees after a background is written, unless the caller keeps it, and says which in the log and the outcome.")]
	public void Complete_Should_Turn_The_Panel_Icon_Off_Unless_Kept(bool keep, int expectedWrites, string expectedLine,
		string expectedState) {
		IFeatureStateService featureState = Substitute.For<IFeatureStateService>();
		ILogger logger = Substitute.For<ILogger>();

		ThemeBackgroundOutcome outcome = new ThemeBackgroundWriter(featureState, Substitute.For<IPackageDataBinder>(),
			Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), logger).Complete(
			new ThemeBackgroundArguments(Color: "#123", KeepIconBackground: keep), "d53a816b-65ed-4b5b-ad3b-f739280add45",
			null);

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

		ThemeBackgroundOutcome outcome = new ThemeBackgroundWriter(Substitute.For<IFeatureStateService>(), binder,
			Substitute.For<IApplicationClient>(), Substitute.For<IServiceUrlBuilder>(), logger).Complete(
			new ThemeBackgroundArguments(ImageId: "6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81", KeepIconBackground: true),
			"d53a816b-65ed-4b5b-ad3b-f739280add45", "UsrThemes");

		logger.Received(1).WriteInfo("Bound the background image into package 'UsrThemes'.");
		outcome.ImagePackage.Should().Be("UsrThemes");
	}

	[TestCase("{\"success\":true,\"rows\":[{\"Id\":\"6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81\"}]}", true)]
	[TestCase("{\"success\":true,\"rows\":[]}", false)]
	[Description("Confirms a background-image-id only when the environment has that SysImage row.")]
	public void TryConfirmImage_Should_Require_The_SysImage_Row(string selectResponse, bool expected) {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(default, default, default, default, default).ReturnsForAnyArgs(selectResponse);
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("http://stub/0/DataService/json/SyncReply/SelectQuery");

		bool confirmed = new ThemeBackgroundWriter(Substitute.For<IFeatureStateService>(), Substitute.For<IPackageDataBinder>(),
			applicationClient, urlBuilder, Substitute.For<ILogger>()).TryConfirmImage(
			new ThemeBackgroundArguments(ImageId: "6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81"), out _);

		confirmed.Should().Be(expected);
	}

	[Test]
	[Description("Refuses the image without throwing when the SysImage query fails, so an unchecked id never reaches the theme write.")]
	public void TryConfirmImage_Should_Refuse_The_Image_When_The_Query_Throws() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(default, default, default, default, default)
			.ThrowsForAnyArgs(new InvalidOperationException("connection reset"));

		bool confirmed = CreateWriter(applicationClient: applicationClient).TryConfirmImage(
			new ThemeBackgroundArguments(ImageId: ImageId), out string error);

		confirmed.Should().BeFalse(because: "an image the environment could not confirm must stop the write");
		error.Should().StartWith($"Could not check image '{ImageId}'",
			because: "the caller must learn that the check, not the image, failed");
	}

	[Test]
	[Description("Asks SysImage for the row whose Id is the canonical form of the given image id, so a differently spelled id cannot confirm another image.")]
	public void TryConfirmImage_Should_Query_SysImage_By_The_Canonical_Image_Id() {
		string body = null;
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(default, default, default, default, default).ReturnsForAnyArgs(call => {
			body = call.ArgAt<string>(1);
			return "{\"success\":true,\"rows\":[]}";
		});

		CreateWriter(applicationClient: applicationClient).TryConfirmImage(
			new ThemeBackgroundArguments(ImageId: " {6D2E4B1F-93A8-4C57-B0E1-2F7A9C3D5E81} "), out _);

		JsonElement query = JsonDocument.Parse(body).RootElement;
		JsonElement filter = query.GetProperty("filters").GetProperty("items").GetProperty("filter0");
		new {
			Schema = query.GetProperty("rootSchemaName").GetString(),
			Column = filter.GetProperty("leftExpression").GetProperty("columnPath").GetString(),
			Value = filter.GetProperty("rightExpression").GetProperty("parameter").GetProperty("value").GetString()
		}.Should().BeEquivalentTo(new { Schema = "SysImage", Column = "Id", Value = ImageId },
			because: "only the SysImage row with this exact id may confirm the image");
	}

	[Test]
	[Description("Strips a credential from the server message it puts in the image check error, because the error reaches the log and the MCP transcript.")]
	public void TryConfirmImage_Should_Redact_A_Credential_In_The_Error() {
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		applicationClient.ExecutePostRequest(default, default, default, default, default)
			.ThrowsForAnyArgs(new InvalidOperationException(MessageWithCredential));

		CreateWriter(applicationClient: applicationClient).TryConfirmImage(
			new ThemeBackgroundArguments(ImageId: ImageId), out string error);

		error.Should().NotContain("hunter2", because: "a credential in a server message must not reach the caller");
	}

	[Test]
	[Description("Turns a failure to switch the panel icon background off into a warning and reports no panel icon state, because the theme is already written.")]
	public void Complete_Should_Warn_When_Turning_The_Panel_Icon_Off_Fails() {
		IFeatureStateService featureState = Substitute.For<IFeatureStateService>();
		featureState.WhenForAnyArgs(service => service.SetFeatureState(default, default, default, default))
			.Do(_ => throw new InvalidOperationException("feature service unavailable"));

		ThemeBackgroundOutcome outcome = CreateWriter(featureState: featureState).Complete(
			new ThemeBackgroundArguments(Color: "#123"), ThemeId, null);

		outcome.Should().Match<ThemeBackgroundOutcome>(value => value.PanelIconBackground == null
				&& value.Warnings.Count == 1
				&& value.Warnings[0].StartsWith(
					"The theme background was applied, but turning off the UsePanelIconBackground feature failed"),
			because: "a failure after the theme write is a warning, and the panel icon state is unknown");
	}

	[Test]
	[Description("Turns a failure to open the theme's package into a warning and reports no image package, because the theme is already written.")]
	public void Complete_Should_Warn_When_Using_The_Theme_Package_Fails() {
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();
		binder.UsePackage(default).ThrowsForAnyArgs(new InvalidOperationException("package is locked"));

		ThemeBackgroundOutcome outcome = CreateWriter(binder: binder).Complete(
			new ThemeBackgroundArguments(ImageId: ImageId, KeepIconBackground: true), ThemeId, "UsrThemes");

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

		ThemeBackgroundOutcome outcome = CreateWriter(binder: binder).Complete(
			new ThemeBackgroundArguments(ImageId: ImageId, KeepIconBackground: true), ThemeId, "UsrThemes");

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

		ThemeBackgroundOutcome outcome = CreateWriter(featureState, binder).Complete(
			new ThemeBackgroundArguments(ImageId: ImageId), ThemeId, "UsrThemes");

		outcome.Warnings.Should().HaveCount(2).And.NotContain(warning => warning.Contains("hunter2"),
			because: "a credential in a server message must not reach the caller");
	}

	[Test]
	[Description("Leaves the panel icon feature and the theme's package alone when the background is removed.")]
	public void Complete_Should_Not_Touch_The_Feature_Or_The_Package_When_The_Background_Is_Removed() {
		IFeatureStateService featureState = Substitute.For<IFeatureStateService>();
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();

		ThemeBackgroundOutcome outcome = CreateWriter(featureState, binder).Complete(
			new ThemeBackgroundArguments(Remove: true), ThemeId, "UsrThemes");

		featureState.DidNotReceiveWithAnyArgs().SetFeatureState(default, default, default, default);
		binder.DidNotReceiveWithAnyArgs().UsePackage(default);
		binder.DidNotReceiveWithAnyArgs().BindRow(default, default, default, default);
		outcome.Should().BeEquivalentTo(ThemeBackgroundOutcome.None,
			because: "a removal neither switches the feature nor binds anything");
	}

	private static ThemeBackgroundWriter CreateWriter(IFeatureStateService featureState = null,
		IPackageDataBinder binder = null, IApplicationClient applicationClient = null) {
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<ServiceUrlBuilder.KnownRoute>()).Returns("http://stub/0/DataService/json/SyncReply/SelectQuery");
		return new ThemeBackgroundWriter(featureState ?? Substitute.For<IFeatureStateService>(),
			binder ?? Substitute.For<IPackageDataBinder>(), applicationClient ?? Substitute.For<IApplicationClient>(),
			urlBuilder, Substitute.For<ILogger>());
	}
}
