using CommandLine;

namespace Clio.Command;

[Verb("uninstall-creatio", HelpText = "Uninstall local instance of creatio")]
public class UninstallCreatioCommandOptions : EnvironmentNameOptions
{

	

}

public class UninstallCreatioCommand : Command<UninstallCreatioCommandOptions>
{

	public override int Execute(UninstallCreatioCommandOptions options){
		return 0;
	}

}