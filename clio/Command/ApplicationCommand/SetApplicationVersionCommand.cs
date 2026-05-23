using Clio.ComposableApplication;
using CommandLine;
using Terrasoft.Common;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command.ApplicationCommand;

[Verb("set-app-version", Aliases = ["appversion"], HelpText = "Set application version")]
internal class SetApplicationVersionOption{
	#region Properties: Public

	[Option('v', "app-version", Required = true, HelpText = "Application version")]
	public string Version { get; internal set; }

	[Value(0, MetaName = "workspace", Required = false, HelpText = "Workspace folder path")]
	public string WorspaceFolderPath { get; internal set; }

	[Option('p', "package-name", Required = false, HelpText = "Package name")]
	public string PackageName { get; internal set; }

	[Option('f', "package-folder", Required = false, HelpText = "Package folder path")]
	public string PackageFolderPath { get; internal set; }

	#endregion
}

internal class SetApplicationVersionCommand : Command<SetApplicationVersionOption>{
	#region Fields: Private

	private readonly IComposableApplicationManager _composableApplicationManager;
	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public SetApplicationVersionCommand(IComposableApplicationManager composableApplicationManager,
		IFileSystem fileSystem) {
		_composableApplicationManager = composableApplicationManager;
		_fileSystem = fileSystem;
	}

	#endregion

	#region Methods: Public

	public override int Execute(SetApplicationVersionOption options) {
		string packagesFolderPath = options.PackageFolderPath.IsNotNullOrEmpty()
			? options.PackageFolderPath
			: _fileSystem.Path.Combine(options.WorspaceFolderPath, "packages");
		_composableApplicationManager.SetVersion(packagesFolderPath, options.Version, options.PackageName);
		return 0;
	}

	#endregion
}
