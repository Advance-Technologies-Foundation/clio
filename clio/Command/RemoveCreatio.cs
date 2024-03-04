using System;
using System.Collections.ObjectModel;
using Clio.Common;
using Clio.Requests;
using Clio.Utilities;
using CommandLine;

namespace Clio.Command;

#region Class: RemoveCreatioCommandOptions

[Verb("RemoveCreatio", Aliases = new string[] {"rm"}, HelpText = "Remove Creatio from Disk")]
public class RemoveCreatioCommandOptions : EnvironmentOptions { }

#endregion

#region Class: RemoveCreatioCommand

public class RemoveCreatioCommand : Command<RemoveCreatioCommandOptions>
{

	private readonly IPowerShellFactory _psf;
	private readonly ILogger _logger;

	#region Constructors: Public

	public RemoveCreatioCommand(ILogger logger){
		_logger = logger;
	}

	#endregion

	#region Methods: Public
	
	public override int Execute(RemoveCreatioCommandOptions options){

		//1. Use IIS to find path to rootFolder
		//2. Read appsettings to find out db type, name etc
		//3. Use appcmd.exe to remove WebSite and appPool if necessary
		//4. Drop db
		//5. Remove files from disk
		//6. Unregister application from Clio
		
		
		
		
		var x = "";
		
		
		
		
		
		//Implement your command her
		throw new NotImplementedException("Command not implemented");
		_logger.WriteInfo("DONE");
		return 0;
	}

	#endregion

}

#endregion