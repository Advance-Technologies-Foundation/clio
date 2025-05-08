using System;
using System.Text;
using Clio.UserEnvironment;
using CommandLine;

namespace Clio.Command;

[Verb("show-web-app-list", Aliases = new[] { "envs", "show-web-app" },
    HelpText = "Show the list of web applications and their settings")]
public class AppListOptions
{
    [Value(0, MetaName = "App name", Required = false, HelpText = "Name of application")]
    public string Name { get; set; }

    [Option('s', "short", Required = false, HelpText = "Show short list")]
    public bool ShowShort { get; set; }
}

public class ShowAppListCommand(ISettingsRepository settingsRepository) : Command<AppListOptions>
{
    private readonly ISettingsRepository _settingsRepository = settingsRepository;

    public override int Execute(AppListOptions options)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            _settingsRepository.ShowSettingsTo(Console.Out, options.Name, options.ShowShort);
            return 0;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return 1;
        }
    }
}
