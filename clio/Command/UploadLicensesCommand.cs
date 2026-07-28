namespace Clio.Command.PackageCommand
{
	using Clio.Common;
	using IFileSystem = System.IO.Abstractions.IFileSystem;

	public class UploadLicensesCommand : RemoteCommand<UploadLicensesOptions>
	{

		private readonly IFileSystem _fileSystem;

		public UploadLicensesCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
			IFileSystem fileSystem)
			: base(applicationClient, settings) {
			_fileSystem = fileSystem;
		}

		protected override string ServicePath => @"/ServiceModel/LicenseService.svc/UploadLicenses";

		protected override string GetRequestData(UploadLicensesOptions options) {
			string fileBody = _fileSystem.File.ReadAllText(options.FilePath);
			fileBody = fileBody.Replace("\"", "\\\"");
			return "{\"licData\":\"" + fileBody + "\"}";
		}

		protected override void ProceedResponse(string response, UploadLicensesOptions options) {
			LicenseResponseParser.EnsureSuccess(response, "License not installed");
			base.ProceedResponse(response, options);
		}
	}

}
