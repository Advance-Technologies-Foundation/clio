using System;
using System.IO;
using Clio.Command.ApplicationCommand;
using Clio.ComposableApplication;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.ApplicationCommand;

internal class SetApplicationIconCommandTestCase : BaseCommandTests<SetApplicationVersionOption>
{

	#region Fields: Private

	private static readonly string MockWorkspacePath = Path.Combine("C:", "MockWorkspaceFolder");

	private static readonly string MockWorkspaceAppPackageFolderPath
		= Path.Combine(MockWorkspacePath, "packages", "IFrameSample");

	private IComposableApplicationManager _composableApplicationManager;

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder){
		_composableApplicationManager = Substitute.For<IComposableApplicationManager>();
		containerBuilder.AddSingleton(_composableApplicationManager);
		base.AdditionalRegistrations(containerBuilder);
	}

	#endregion

	[Test]
	public void SetApplicationIconCommand_CallsComposableAppmanager(){
		string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Files", "icon.svg");
		string appName = "ExampleAppName";
		SetApplicationIconCommand command = Container.GetRequiredService<SetApplicationIconCommand>();
		command.Execute(new SetApplicationIconOption {
			IconPath = iconPath,
			AppPath = MockWorkspaceAppPackageFolderPath,
			AppName = appName
		});
		_composableApplicationManager.Received(1).SetIcon(MockWorkspaceAppPackageFolderPath, iconPath, appName);
	}

}