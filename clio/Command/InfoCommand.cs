using System;
using System.Reflection;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("info", Aliases = new[]
{
    "ver", "get-version", "i"
}, HelpText = "Check for Creatio packages updates in NuGet")]
public class InfoCommandOptions
{

    #region Properties: Public

    [Option("all", Required = false, HelpText = "Get versions for all known components")]
    public bool All { get; set; }

    [Option("clio", Required = false, HelpText = "Get clio version")]
    public bool Clio { get; set; }

    [Option("gate", Required = false, HelpText = "Get clio-gate version")]
    public bool Gate { get; set; }

    [Option("runtime", Required = false, HelpText = "Get dotnet version")]
    public bool Runtime { get; set; }

    [Option('s', "settings-file", Required = false, HelpText = "Get path to settings file")]
    public bool ShowSettingsFilePath { get; set; }

    #endregion

}

public class InfoCommand : Command<InfoCommandOptions>
{

    #region Constants: Private

    private const string _gateVersion = "2.0.0.34";

    #endregion

    #region Fields: Private

    private readonly ILogger _logger;

    #endregion

    #region Constructors: Public

    public InfoCommand(ILogger logger)
    {
        _logger = logger;
    }

    #endregion

    #region Methods: Public

    public override int Execute(InfoCommandOptions options)
    {
        if (options is object && options.Clio)
        {
            _logger.WriteInfo($"clio:   {Assembly.GetEntryAssembly().GetName().Version}");
            return 0;
        }
        if (options is object && options.Runtime)
        {
            _logger.WriteInfo($"dotnet: {Environment.Version.ToString()}");
            return 0;
        }
        if (options is object && options.Gate)
        {
            _logger.WriteInfo($"gate:   {_gateVersion}");
            return 0;
        }
        if (options.ShowSettingsFilePath)
        {
            _logger.WriteInfo(SettingsRepository.AppSettingsFile);
            return 0;
        }
        if ((options is object && options.All) ||
            (!options.Runtime && !options.Gate && !options.Clio && !options.ShowSettingsFilePath))
        {
            _logger.WriteInfo($"clio:   {Assembly.GetEntryAssembly().GetName().Version}");
            _logger.WriteInfo($"gate:   {_gateVersion}");
            _logger.WriteInfo($"dotnet:   {Environment.Version.ToString()}");
            _logger.WriteInfo($"settings file path: {SettingsRepository.AppSettingsFile}");
            return 0;
        }
        return 1;
    }

    #endregion

}
