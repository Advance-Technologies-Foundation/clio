using Clio.Command;
using Clio.Command.Theming;
using Clio.Common;
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

		new ThemeBackgroundWriter(featureState, Substitute.For<IPackageDataBinder>()).Complete(
			new ThemeBackgroundArguments(Color: "#123", KeepIconBackground: keep), "d53a816b-65ed-4b5b-ad3b-f739280add45",
			null);

		featureState.Received(expectedWrites).SetFeatureState("UsePanelIconBackground", SysAdminUnitIds.AllEmployees, false);
	}
}
