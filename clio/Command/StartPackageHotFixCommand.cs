using Clio.Package;
using CommandLine;

namespace Clio.Command;

[Verb("start-pkg-hotfix", Aliases = new[] {"hotfix-start"}, HelpText = "Starts hotfix state for package.")]
public class StartPackageHotFixCommandOptions : EnvironmentOptions
{

	#region Properties: Public

	[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
	public string PackageName { get; set; }

	#endregion

}

public class StartPackageHotFixCommand : RemoteCommand<StartPackageHotFixCommandOptions>
{

	#region Fields: Private

	private readonly IPackageEditableMutator _packageEditableMutator;

	#endregion

	#region Constructors: Public

	public StartPackageHotFixCommand(IPackageEditableMutator packageEditableMutator,
		EnvironmentSettings environmentSettings)
		: base(environmentSettings){
		_packageEditableMutator = packageEditableMutator;
	}

	#endregion

	#region Methods: Public

	public override int Execute(StartPackageHotFixCommandOptions commandOptions){
		Logger.WriteInfo($"Starts hotfix state for package: \"{commandOptions.PackageName}\"");
		_packageEditableMutator.StartPackageHotfix(commandOptions.PackageName);
		Logger.WriteInfo($"Hotfix mode successfully applied to {commandOptions.PackageName} package");
		return 0;
	}

	#endregion

}