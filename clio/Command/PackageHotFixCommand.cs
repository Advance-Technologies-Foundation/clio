using Clio.Package;
using CommandLine;

namespace Clio.Command;

[Verb("pkg-hotfix", Aliases = new[] {"hotfix", "hf"}, HelpText = "Enable/disable hotfix state for package.")]
public class PackageHotFixCommandOptions : EnvironmentOptions
{

	#region Properties: Public

	[Value(0, MetaName = "PackageName", Required = true, HelpText = "Package name")]
	public string PackageName { get; set; }
	
	[Value(1, MetaName = "HotFixState", Required = true, HelpText = "HotFix state")]
	public bool Enable { get; internal set; }

	#endregion

}

public class PackageHotFixCommand : RemoteCommand<PackageHotFixCommandOptions>
{

	#region Fields: Private

	private readonly IPackageEditableMutator _packageEditableMutator;

	#endregion

	#region Constructors: Public

	public PackageHotFixCommand(IPackageEditableMutator packageEditableMutator,
		EnvironmentSettings environmentSettings)
		: base(environmentSettings){
		_packageEditableMutator = packageEditableMutator;
	}

	#endregion

	#region Methods: Public

	public override int Execute(PackageHotFixCommandOptions commandOptions){
		if (commandOptions.Enable) {
			Logger.WriteInfo($"Enable hotfix state for package: \"{commandOptions.PackageName}\"");
		} else {
			Logger.WriteInfo($"Disable hotfix state for package: \"{commandOptions.PackageName}\"");
		}	
		_packageEditableMutator.SetPackageHotfix(commandOptions.PackageName, commandOptions.Enable);
		Logger.WriteInfo($"Done");
		return 0;
	}

	#endregion

}