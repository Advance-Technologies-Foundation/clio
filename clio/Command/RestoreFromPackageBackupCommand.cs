using CommandLine;
using Common;

namespace Clio.Command;

[Verb("restore-configuration", Aliases = new string[] { "restore", "rc" },
    HelpText = "Restore configuration from last backup")]
public class RestoreFromPackageBackupOptions : RemoteCommandOptions
{
    [Option('d', "skip-rollback-data", Required = false,
        HelpText = "Skip rollback data", Default = false)]
    public bool InstallPackageData { get; set; }

    [Option('f', "force", Required = false,
        HelpText = "Restore configuration without sql backward compatibility check", Default = false)]
    public bool IgnoreSqlScriptBackwardCompatibilityCheck { get; set; }
}

internal class RestoreFromPackageBackupCommand(IApplicationClient applicationClient, EnvironmentSettings settings): RemoteCommand<RestoreFromPackageBackupOptions>(applicationClient, settings)
{
    protected override string ServicePath => @"/ServiceModel/PackageInstallerService.svc/RestoreFromPackageBackup";

    protected override string GetRequestData(RestoreFromPackageBackupOptions options) =>
        "{\"installPackageData\": " + options.InstallPackageData.ToString().ToLower() +
        ", \"ignoreSqlScriptBackwardCompatibilityCheck\": " +
        options.IgnoreSqlScriptBackwardCompatibilityCheck.ToString().ToLower() + "}";
}
