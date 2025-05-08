using CommandLine;

namespace Clio.Command;

[Verb("install-tide", Aliases = new[]
{
    "tide", "itide"
}, HelpText = "Install T.I.D.E. to the environment")]
public class InstallTideCommandOptions : EnvironmentNameOptions
{ }

internal class InstallTideCommand : Command<InstallTideCommandOptions>
{

    #region Fields: Private

    private readonly InstallNugetPackageCommand _installNugetPackageCommand;

    #endregion

    #region Constructors: Public

    public InstallTideCommand(InstallNugetPackageCommand installNugetPackageCommand)
    {
        _installNugetPackageCommand = installNugetPackageCommand;
    }

    #endregion

    #region Methods: Public

    public override int Execute(InstallTideCommandOptions options)
    {
        InstallNugetPkgOptions installNugetPackageCommandOptions = new()
        {
            Names = "atftide"
        };
        installNugetPackageCommandOptions.CopyFromEnvironmentSettings(options);
        return _installNugetPackageCommand.Execute(installNugetPackageCommandOptions);
    }

    #endregion

}
