namespace Clio.Tests.Command;

using Clio.Command;
using Clio.Command.Branding;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

[TestFixture]
[Property("Module", "Command")]
public sealed class RemoveBackgroundCommandTests : BaseCommandTests<RemoveBackgroundOptions>
{
	private const string ThemeId = "fb73e945-fe28-4834-838b-3986693c1a3d";

	private IApplicationClient _applicationClient;
	private RemoveBackgroundCommand _command;

	public override void Setup() {
		base.Setup();
		_command = Container.GetRequiredService<RemoveBackgroundCommand>();
		_command.Logger = Substitute.For<ILogger>();
	}

	public override void TearDown() {
		_applicationClient.ClearReceivedCalls();
		base.TearDown();
	}

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		_applicationClient = Substitute.For<IApplicationClient>();
		containerBuilder.AddTransient<IApplicationClient>(_ => _applicationClient);
	}

	[Test]
	[Description("Sends an empty background to UpdateTheme, which the platform reads as 'remove the stored background', while null would keep it.")]
	public void Execute_ShouldSendEmptyBackgroundToUpdateTheme() {
		_command.Execute(new RemoveBackgroundOptions { Theme = ThemeId });

		_applicationClient.Received(1).ExecutePostRequest(Arg.Is<string>(url => url.EndsWith("ThemeService.svc/UpdateTheme")),
			"{\"id\":\"" + ThemeId + "\",\"background\":{}}", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}
}
