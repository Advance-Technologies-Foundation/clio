namespace Clio.Command.PackageCommand
{
	using Clio.Common;

	public class DeletePackageCommand : RemoteCommand<DeletePkgOptions>
	{

		public DeletePackageCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string ServicePath => @"/ServiceModel/AppInstallerService.svc/DeletePackage";

		protected override string GetResponseData(DeletePkgOptions options) {
			return "\"" + options.Name + "\"";
		}

	}
}
