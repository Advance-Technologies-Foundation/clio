using System;
using System.Threading;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("turn-fsm", Aliases = new[]
{
    "tfsm", "fsm"
}, HelpText = "Turn file system mode on or off for an environment")]
public class TurnFsmCommandOptions : SetFsmConfigOptions
{ }

public class TurnFsmCommand : Command<TurnFsmCommandOptions>
{

    #region Fields: Private

    private readonly SetFsmConfigCommand _setFsmConfigCommand;
    private readonly LoadPackagesToFileSystemCommand _loadPackagesToFileSystemCommand;
    private readonly LoadPackagesToDbCommand _loadPackagesToDbCommand;
    private readonly IApplicationClient _applicationClient;
    private readonly EnvironmentSettings _environmentSettings;

    #endregion

    #region Constructors: Public

    public TurnFsmCommand(SetFsmConfigCommand setFsmConfigCommand,
        LoadPackagesToFileSystemCommand loadPackagesToFileSystemCommand,
        LoadPackagesToDbCommand loadPackagesToDbCommand, IApplicationClient applicationClient,
        EnvironmentSettings environmentSettings)
    {
        _setFsmConfigCommand = setFsmConfigCommand;
        _loadPackagesToFileSystemCommand = loadPackagesToFileSystemCommand;
        _loadPackagesToDbCommand = loadPackagesToDbCommand;
        _applicationClient = applicationClient;
        _environmentSettings = environmentSettings;
    }

    #endregion

    #region Methods: Public

    public override int Execute(TurnFsmCommandOptions options)
    {
        if (options.IsFsm == "on")
        {
            if (_setFsmConfigCommand.Execute(options) == 0)
            {
                options.IsNetCore = _environmentSettings.IsNetCore;
                if (options.IsNetCore == true)
                {
                    RestartOptions opt = new()
                    {
                        Environment = options.Environment,
                        Uri = options.Uri,
                        Login = options.Login,
                        Password = options.Password,
                        IsNetCore = options.IsNetCore
                    };
                    RestartCommand restartCommand = new(_applicationClient, _environmentSettings);
                    restartCommand.Execute(opt);
                    Thread.Sleep(TimeSpan.FromSeconds(3));
                    _applicationClient.Login();
                }
                return _loadPackagesToFileSystemCommand.Execute(options);
            }
        }
        else
        {
            if (_loadPackagesToDbCommand.Execute(options) == 0)
            {
                return _setFsmConfigCommand.Execute(options);
            }
        }
        return 1;
    }

    #endregion

}
