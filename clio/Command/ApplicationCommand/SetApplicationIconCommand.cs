using Clio.ComposableApplication;
using CommandLine;

namespace Clio.Command.ApplicationCommand;

[Verb("set-app-icon", Aliases = new[] { "appicon", "ai", "set-icon" }, HelpText = "Set application icon")]
internal class SetApplicationIconOption
{
    [Option('p', "app-name", Required = false, HelpText = "App name")]
    public string AppName { get; internal set; }

    [Option('i', "app-icon", Required = true, HelpText = "Application icon path")]
    public string IconPath { get; internal set; }

    [Option('f', "app-path", Required = false, HelpText = "Path to application package folder or archive")]
    public string AppPath { get; internal set; }
}

internal class SetApplicationIconCommand(IComposableApplicationManager composableApplicationManager)
    : Command<SetApplicationIconOption>
{
    private readonly IComposableApplicationManager _composableApplicationManager = composableApplicationManager;

    public override int Execute(SetApplicationIconOption options)
    {
        _composableApplicationManager.SetIcon(options.AppPath, options.IconPath, options.AppName);
        return 0;
    }
}
