using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("open-settings", Aliases = ["conf", "configuration", "settings", "os"],
    HelpText = "Open configuration file")]
public class OpenCfgOptions { }

public class OpenCfgCommand : Command<OpenCfgOptions> {

    #region Fields: Private

    private readonly ILogger _logger;

    #endregion

    #region Constructors: Public

    public OpenCfgCommand(ILogger logger){
        _logger = logger;
    }

    #endregion

    #region Methods: Public

    public override int Execute(OpenCfgOptions options){
        try {
            SettingsRepository.OpenSettingsFile();
            return 0;
        } catch (Exception e) {
            _logger.WriteInfo($"{e.Message}");
            return 1;
        }
    }

    #endregion

}