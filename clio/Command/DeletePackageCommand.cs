namespace Clio.Command.PackageCommand;

using Common;

public class DeletePackageCommand : RemoteCommand<DeletePkgOptions>
{
    public DeletePackageCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
        : base(applicationClient, settings)
    {
    }

    protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/DeletePackage";

    protected override string GetRequestData(DeletePkgOptions options) => "\"" + options.Name + "\"";
}
