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

	[TestCase(false, 1)]
	[TestCase(true, 0)]
	[Description("Turns the panel icon background off for All employees after a background is written, unless the caller keeps it.")]
	public void Complete_Should_Turn_The_Panel_Icon_Off_Unless_Kept(bool keep, int expectedWrites) {
		IFeatureStateService featureState = Substitute.For<IFeatureStateService>();

		new ThemeBackgroundWriter(featureState, Substitute.For<IPackageDataBinder>(), Substitute.For<IApplicationClient>(),
			Substitute.For<IServiceUrlBuilder>()).Complete(
			new ThemeBackgroundArguments(Color: "#123", KeepIconBackground: keep), "d53a816b-65ed-4b5b-ad3b-f739280add45",
			null);

		featureState.Received(expectedWrites).SetFeatureState("UsePanelIconBackground", SysAdminUnitIds.AllEmployees, false);
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
			applicationClient, urlBuilder).TryConfirmImage(
			new ThemeBackgroundArguments(ImageId: "6d2e4b1f-93a8-4c57-b0e1-2f7a9c3d5e81"), out _);

		confirmed.Should().Be(expected);
	}
}
