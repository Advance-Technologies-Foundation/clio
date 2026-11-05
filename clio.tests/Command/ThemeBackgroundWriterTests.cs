using Clio.Command;
using Clio.Command.Theming;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class ThemeBackgroundWriterTests {

	[TestCase(false, 1, "UsePanelIconBackground is off for All employees.")]
	[TestCase(true, 0, "UsePanelIconBackground was left as it is (keep-icon-background).")]
	[Description("Turns the panel icon background off for All employees after a background is written, unless the caller keeps it, and says which.")]
	public void Complete_Should_Turn_The_Panel_Icon_Off_Unless_Kept(bool keep, int expectedWrites, string expectedLine) {
		IFeatureStateService featureState = Substitute.For<IFeatureStateService>();
		ILogger logger = Substitute.For<ILogger>();

		new ThemeBackgroundWriter(featureState, Substitute.For<IPackageDataBinder>(), Substitute.For<IApplicationClient>(),
			Substitute.For<IServiceUrlBuilder>(), logger).Complete(
			new ThemeBackgroundArguments(Color: "#123", KeepIconBackground: keep), "d53a816b-65ed-4b5b-ad3b-f739280add45",
			null);

		featureState.Received(expectedWrites).SetFeatureState("UsePanelIconBackground", SysAdminUnitIds.AllEmployees, false);
		logger.Received(1).WriteInfo(expectedLine);
	}

	[Test]
	[Description("Names the package the background image was bound into.")]
	public void Complete_Should_Name_The_Package_It_Binds_The_Image_Into() {
		IPackageDataBinder binder = Substitute.For<IPackageDataBinder>();
		binder.UsePackage("UsrThemes").Returns("UsrThemes");
		binder.BindRow(default, default, default, default).ReturnsForAnyArgs(PackageDataBindingOutcome.Success());
		ILogger logger = Substitute.For<ILogger>();

		new ThemeBackgroundWriter(Substitute.For<IFeatureStateService>(), binder, Substitute.For<IApplicationClient>(),
			Substitute.For<IServiceUrlBuilder>(), logger).Complete(
			new ThemeBackgroundArguments(ImageId: "6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81", KeepIconBackground: true),
			"d53a816b-65ed-4b5b-ad3b-f739280add45", "UsrThemes");

		logger.Received(1).WriteInfo("Bound the background image into package 'UsrThemes'.");
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
}
