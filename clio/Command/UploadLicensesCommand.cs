namespace Clio.Command.PackageCommand
{
	using System.IO;
	using System.Text;
	using Clio.Common;

	public class UploadLicensesCommand : RemoteCommand<UploadLicensesOptions>
	{

		public UploadLicensesCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string ServicePath => @"/ServiceModel/LicenseService.svc/UploadLicenses";

		protected override string GetRequestData(UploadLicensesOptions options) {
			string fileBody = File.ReadAllText(options.FilePath);
			fileBody = fileBody.Replace("\"", "\\\"");
			return "{\"licData\":\"" + fileBody + "\"}";
		}

	}
}
