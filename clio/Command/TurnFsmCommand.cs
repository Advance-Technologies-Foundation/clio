using CommandLine.Text;
using CommandLine;
using Newtonsoft.Json;
using Clio.UserEnvironment;
using FluentValidation;

namespace Clio.Command;


[Verb("turn-fsm", Aliases = new[] {"tfsm", "fsm"}, HelpText = "Turn file system mode on or off for an environment")]
public class TurnFsmCommandOptions : SetFsmConfigOptions
{
}

public class TurnFsmCommand : Command<TurnFsmCommandOptions>
{

	private readonly SetFsmConfigCommand _setFsmConfigCommand;
	private readonly LoadPackagesToFileSystemCommand _loadPackagesToFileSystemCommand;
	private readonly LoadPackagesToDbCommand _loadPackagesToDbCommand;

	public TurnFsmCommand(SetFsmConfigCommand setFsmConfigCommand, LoadPackagesToFileSystemCommand loadPackagesToFileSystemCommand, 
		LoadPackagesToDbCommand loadPackagesToDbCommand) {
		_setFsmConfigCommand = setFsmConfigCommand;
		_loadPackagesToFileSystemCommand = loadPackagesToFileSystemCommand;
		_loadPackagesToDbCommand = loadPackagesToDbCommand;
	}

	public override int Execute(TurnFsmCommandOptions options) {

		if(options.IsFsm == "on") {
			if(_setFsmConfigCommand.Execute(options)==0)
			{
				return _loadPackagesToFileSystemCommand.Execute(options);
			}
		} 
		else {
			if(_loadPackagesToDbCommand.Execute(options) == 0) {

				return _setFsmConfigCommand.Execute(options);
			}
		}
		return 1;
	}
}