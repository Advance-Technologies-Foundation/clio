namespace Clio.Command.PackageCommand
{
    using Clio.Common;
    using System;

	public class DeletePackageCommand: BaseRemoteCommand
	{

		public DeletePackageCommand(IApplicationClient applicationClient) 
			: base(applicationClient) {
		}

		private static string DeletePackageUrl => _appUrl + @"/ServiceModel/AppInstallerService.svc/DeletePackage";

		private void DeletePackage(string code) {
			Console.WriteLine("Deleting...");
			string deleteRequestData = "\"" + code + "\"";
			ApplicationClient.ExecutePostRequest(DeletePackageUrl, deleteRequestData, 600000);
			Console.WriteLine("Deleted.");
		}

		public int Delete(DeletePkgOptions options) {
			try {
				Configure(options);
				DeletePackage(options.Name);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}
	}
}
