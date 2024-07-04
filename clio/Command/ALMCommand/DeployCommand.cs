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
	public class DeployCommandOptions : RemoteCommandOptions
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
					Logger.WriteInfo($"File uploaded. FileId: {fileId}");
					var startOperationUrl = _environmentSettings.Uri + startOperationServiceUrl;
					string requestData = "{\"environmentName\": \"" + options.EnvironmentName
						+ "\", \"fileId\": \"" + fileId + "\"}";
					if (StartOperation(startOperationUrl, requestData)) {
						Logger.WriteInfo("Done");
						return 0;
					}
					Logger.WriteError($"Operation not started. FileId: {fileId}");
					return 1;
				}
				Logger.WriteError($"File not uploaded. FileId: {fileId}");
				return 1;
			} catch (Exception e) {
				Logger.WriteError(e.Message);
				return 1;
			}
		}

		private bool UploadFile(string uploadLicenseUrl, string filePath, Guid fileId) {
			FileInfo fi = new FileInfo(filePath);
			var uploadLicenseEnpointUrl = _environmentSettings.Uri + uploadLicenseUrl
					+ "?fileName=" + fi.Name + "&totalFileLength=" + fi.Length + "&fileId=" + fileId;
			Logger.WriteInfo($"Start uploading file {fi.Name}");
			string uploadResult = _applicationClient.UploadAlmFileByChunk(uploadLicenseEnpointUrl, filePath);
			Logger.WriteInfo($"End of uploading");
			JObject json = JObject.Parse(uploadResult);
			return json["success"].ToString() == "True";
		}

		private bool StartOperation(string startOperationUrl, string requestData) {
			string result = _applicationClient.ExecutePostRequest(startOperationUrl, requestData);
			JObject startOperationResult = JObject.Parse(result);
			if (startOperationResult["success"].ToString() == "True") {
				Logger.WriteInfo($"Command to deploy packages to environmnet succesfully startetd OpeartionId: {startOperationResult["operationId"]}");
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