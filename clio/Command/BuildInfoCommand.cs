using System;
using Clio.Command;
using Clio.Command.CreatioInstallCommand;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;


[Verb("get-build-info", Aliases = new[] {"buildinfo", "bi"}, HelpText = "Deploy Creatio from zip file")]
public class BuildInfoOptions : PfInstallerOptions
{ }

public class BuildInfoCommand
{

	private readonly ICreatioInstallerService _creatioInstallerService;

	public string RemoteArtefactServerPath { get; set; }
	public string ProductFolder { get; set; }
	#region Constructors: Public

	public BuildInfoCommand(ISettingsRepository settingsRepository, ICreatioInstallerService creatioInstallerService){
		_creatioInstallerService = creatioInstallerService;
		RemoteArtefactServerPath = settingsRepository.GetRemoteArtefactServerPath();
		ProductFolder = settingsRepository.GetCreatioProductsFolder();
	}

	#endregion

	#region Methods: Public

	public int Execute(BuildInfoOptions options){
		string buildPath = _creatioInstallerService.GetBuildFilePathFromOptions(options.Product, options.DBType, options.RuntimePlatform);
		Console.WriteLine(buildPath);
		return 0;
	}

	#endregion

}