using Clio.Common;

namespace Clio.Command.PackageCommand;

public class DeletePackageCommand : RemoteCommand<DeletePkgOptions>
{

    #region Constructors: Public

    public DeletePackageCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
        : base(applicationClient, settings)
    { }

    #endregion

    #region Properties: Protected

    protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/DeletePackage";

    #endregion

    #region Methods: Protected

    protected override string GetRequestData(DeletePkgOptions options)
    {
        return "\"" + options.Name + "\"";
    }

    #endregion

}
