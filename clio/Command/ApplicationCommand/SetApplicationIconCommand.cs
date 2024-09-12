using System.IO;
using Clio.ComposableApplication;
using CommandLine;
using Terrasoft.Common;

namespace Clio.Command.ApplicationCommand;

[Verb("set-app-icon", Aliases = new[] {"appicon", "ai", "set-icon"}, HelpText = "Set application icon")]
internal class SetApplicationIconOption
{

	#region Properties: Public

	[Option('p', "app-name", Required = false, HelpText = "App name")]
	public string AppName { get; internal set; }

	[Option('i', "app-icon", Required = true, HelpText = "Application icon path")]
	public string IconPath { get; internal set; }

	[Option('f', "package-folder", Required = false, HelpText = "Package folder path")]
	public string PackageFolderPath { get; internal set; }

	[Value(0, MetaName = "workspace", Required = false, HelpText = "Workspace folder path")]
	public string WorspaceFolderPath { get; internal set; }

	#endregion

}

internal class SetApplicationIconCommand : Command<SetApplicationIconOption>
{

	#region Fields: Private

	private readonly IComposableApplicationManager _composableApplicationManager;

	#endregion

	#region Constructors: Public

	public SetApplicationIconCommand(IComposableApplicationManager composableApplicationManager){
		_composableApplicationManager = composableApplicationManager;
	}

	#endregion

	#region Methods: Public

	public override int Execute(SetApplicationIconOption options){
		string packagesFolderPath = options.PackageFolderPath.IsNotNullOrEmpty() ?
			options.PackageFolderPath : Path.Combine(options.WorspaceFolderPath, "packages");
		_composableApplicationManager.SetIcon(packagesFolderPath, options.IconPath, options.AppName);
		return 0;
	}

	#endregion

}