using System;
using Clio.Command;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;


[Verb("get-build-info", Aliases = new[] {"buildinfo", "bi"}, HelpText = "Deploy Creatio from zip file")]
public class BuildInfoOptions : PfInstallerOptions
{ }

public class BuildInfoCommand : InstallerCommand
{

	#region Constructors: Public

	public BuildInfoCommand(ISettingsRepository settingsRepository){
		RemoteArtefactServerPath = settingsRepository.GetRemoteArtefactServerPath();
		ProductFolder = settingsRepository.GetCreatioProductsFolder();
	}

	#endregion

	#region Methods: Public

	public int Execute(BuildInfoOptions options){
		string buildPath = GetBuildFilePathFromOptions(options.Product, options.DBType, options.RuntimePlatform);
		Console.WriteLine(buildPath);
		return 0;
	}

	#endregion

}