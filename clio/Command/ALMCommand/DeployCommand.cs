namespace Clio.Command
{
	using System;
	using System.IO;
	using System.Linq;
	using System.Net;
	using System.Text;
	using Clio.Common;
    using CommandLine;
	using Newtonsoft.Json.Linq;

	#region Class: DeployCommandOptions

	[Verb("alm-deploy", Aliases = new string[] { "deploy" }, HelpText = "Install package to selected environment")]
	public class DeployCommandOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "File", Required = true, HelpText = "Package file path")]
		public string FilePath { get; set; }

		[Option('t', "site", Required = true, HelpText = "Site name")]
		public string EnvironmentName { get; set; }

		[Option('g', "general", Required = false, HelpText = "Use non ssp user", Default = false)]
		public bool NonUseSsp { get; set; }

	}

	#endregion

	#region Class: DeployCommand

	public class DeployCommand : RemoteCommand<DeployCommandOptions>
	{

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationClient _applicationClient;

		#endregion

		#region Constructors: Public

		public DeployCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
			settings.CheckArgumentNull(nameof(settings));
			applicationClient.CheckArgumentNull(nameof(applicationClient));
			_environmentSettings = settings;
			_applicationClient = applicationClient;
		}

		#endregion

		#region Methods: Public

		public override int Execute(DeployCommandOptions options) {
			try {
				Guid fileId;
				fileId = GetFileId();
				string subEndpointPath;
				if (options.NonUseSsp) {
					subEndpointPath = "rest";
				} else {
					subEndpointPath = "ssp/rest";
				}
				string uploadLicenseServiceUrl = $"/0/{subEndpointPath}/InstallPackageService/UploadFile";
				string startOperationServiceUrl = $"/0/{subEndpointPath}/InstallPackageService/StartOperation";
				if (UploadFile(uploadLicenseServiceUrl, options.FilePath, fileId)) {
					Console.WriteLine($"File uploaded. FileId: {fileId}");
					var startOperationUrl = _environmentSettings.Uri + startOperationServiceUrl;
					string requestData = "{\"environmentName\": \"" + options.EnvironmentName
						+ "\", \"fileId\": \"" + fileId + "\"}";
					if (StartOperation(startOperationUrl, requestData)) {
						Console.WriteLine("Done");
						return 0;
					}
					Console.WriteLine($"Operation not started. FileId: {fileId}");
					return 1;
				}
				Console.WriteLine($"File not uploaded. FileId: {fileId}");
				return 1;
			} catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}

		private bool UploadFile(string uploadLicenseUrl, string filePath, Guid fileId) {
			FileInfo fi = new FileInfo(filePath);
			var uploadLicenseEnpointUrl = _environmentSettings.Uri + uploadLicenseUrl
					+ "?fileName=" + fi.Name + "&totalFileLength=" + fi.Length + "&fileId=" + fileId;
			string uploadResult = _applicationClient.UploadAlmFile(uploadLicenseEnpointUrl, filePath);
			JObject json = JObject.Parse(uploadResult);
			return json["success"].ToString() == "True";
		}

		private bool StartOperation(string startOperationUrl, string requestData) {
			string result = _applicationClient.ExecutePostRequest(startOperationUrl, requestData);
			JObject startOperationResult = JObject.Parse(result);
			if (startOperationResult["success"].ToString() == "True") {
				Console.WriteLine($"OpeartionId: {startOperationResult["operationId"]}");
				return true;
			}
			return false;
		}

		private static Guid GetFileId() {
			return Guid.NewGuid();
		}

		#endregion

	}

	#endregion

}