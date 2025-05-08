using Common;

namespace Clio.Command.PackageCommand;
public class DeletePackageCommand(IApplicationClient applicationClient, EnvironmentSettings settings): RemoteCommand<DeletePkgOptions>(applicationClient, settings)
{
    protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/DeletePackage";

    protected override string GetRequestData(DeletePkgOptions options) => "\"" + options.Name + "\"";
}
