using CommandLine;

namespace Clio.Command;

[Verb("install-tide", Aliases = new[] { "tide", "itide" }, HelpText = "Install T.I.D.E. to the environment")]
public class InstallTideCommandOptions : EnvironmentNameOptions
{
}

internal class InstallTideCommand(InstallNugetPackageCommand installNugetPackageCommand)
    : Command<InstallTideCommandOptions>
{
    private readonly InstallNugetPackageCommand _installNugetPackageCommand = installNugetPackageCommand;

    public override int Execute(InstallTideCommandOptions options)
    {
        InstallNugetPkgOptions installNugetPackageCommandOptions = new() { Names = "atftide" };
        installNugetPackageCommandOptions.CopyFromEnvironmentSettings(options);
        return _installNugetPackageCommand.Execute(installNugetPackageCommandOptions);
    }
}
