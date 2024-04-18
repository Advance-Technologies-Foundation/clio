using Clio.Package;
using CommandLine;

namespace Clio.Command;

[Verb("finish-pkg-hotfix", Aliases = new [] { "hotfix-finish" }, HelpText = "Finishes hotfix state for package.")]
public class FinishPackageHotFixCommandOptions : EnvironmentOptions
{
	[Value(0, MetaName = "Name", Required = true, HelpText = "Package name")]
	public string PackageName { get; set; }
}

public class FinishPackageHotFixCommand : RemoteCommand<FinishPackageHotFixCommandOptions>
{

	#region Fields: Private

	private readonly IPackageEditableMutator _packageEditableMutator;

	#endregion

	#region Constructors: Public

	public FinishPackageHotFixCommand(IPackageEditableMutator packageEditableMutator,
		EnvironmentSettings environmentSettings)
		: base(environmentSettings){
		_packageEditableMutator = packageEditableMutator;
	}

	#endregion

	#region Methods: Public

	public override int Execute(FinishPackageHotFixCommandOptions commandOptions){
		Logger.WriteInfo($"Ends hotfix state for package: \"{commandOptions.PackageName}\"");
		_packageEditableMutator.FinishPackageHotfix(commandOptions.PackageName);
		Logger.WriteInfo("Done");
		return 0;
	}

	#endregion

}