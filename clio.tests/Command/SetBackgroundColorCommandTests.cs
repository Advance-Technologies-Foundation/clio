namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Command.Branding;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class SetBackgroundColorCommandTests : BaseCommandTests<SetBackgroundColorOptions>
{
	private const string ThemeId = "fb73e945-fe28-4834-838b-3986693c1a3d";

	private IApplicationClient _applicationClient;
	private IFeatureStateService _featureState;
	private SetBackgroundColorCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<SetBackgroundColorCommand>();
		_command.Logger = Substitute.For<ILogger>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		_featureState.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		_featureState = Substitute.For<IFeatureStateService>();
		containerBuilder.AddTransient<IApplicationClient>(_ => _applicationClient);
		containerBuilder.AddTransient<IFeatureStateService>(_ => _featureState);
	}

	[Test]
	[Description("Sends only the theme id and the trimmed color to UpdateTheme, then turns the panel icon background off for All Employees.")]
	public void Execute_ShouldSendColorToUpdateThemeAndTurnOffPanelIcon() {
		_applicationClient.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns("{\"success\":true}");

		_command.Execute(new SetBackgroundColorOptions { Color = " #0A1B2C ", Theme = ThemeId });

		_applicationClient.Received(1).ExecutePostRequest(Arg.Is<string>(url => url.EndsWith("ThemeService.svc/UpdateTheme")),
			"{\"id\":\"" + ThemeId + "\",\"background\":{\"mode\":\"Color\",\"color\":\"#0A1B2C\"}}",
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		_featureState.Received(1).SetFeatureState(SetBackgroundImageCommand.PanelIconBackgroundFeatureCode,
			SysAdminUnitIds.AllEmployees, false);
	}
}
