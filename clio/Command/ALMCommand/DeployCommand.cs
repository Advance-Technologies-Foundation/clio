namespace Clio.Command
{
	using System;
	using System.IO;
	using System.Linq;
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
				do {
					fileId = Guid.NewGuid();
				} while (Char.IsLetter(fileId.ToString().ElementAt(0)));
				string subEndpointPath;
				if (options.NonUseSsp) {
					subEndpointPath = "rest";
				} else {
					subEndpointPath = "ssp/rest";
				}
				string uploadLicenseServiceUrl = $"/0/{subEndpointPath}/InstallPackageService/UploadFile";
				string startOperationServiceUrl = $"/0/{subEndpointPath}/InstallPackageService/StartOperation";
				FileInfo fi = new FileInfo(options.FilePath);
				var uploadLicenseUrl = _environmentSettings.Uri + uploadLicenseServiceUrl
					+ "?fileName=" + fi.Name + "&totalFileLength=" + fi.Length + "&fileId=" + fileId; 
				string uploadResult = _applicationClient.UploadFile(uploadLicenseUrl, options.FilePath);
				JObject json = JObject.Parse(uploadResult);
				if (json["success"].ToString() == "True") {
					Console.WriteLine($"File uploaded. FileId: {fileId}");
					var startOperationUrl = _environmentSettings.Uri + startOperationServiceUrl;
					string requestData = "{\"environmentName\": " + options.EnvironmentName
						+ ", \"fileId\": " + fileId + "}";
					string result = _applicationClient.ExecutePostRequest(startOperationUrl, requestData);
					JObject startOperationResult = JObject.Parse(result);
					if (startOperationResult["success"].ToString() == "True") {
						var operationId = startOperationResult["operationId"].ToString();
						Console.WriteLine($"OpeartionId: {operationId}");
						Console.WriteLine("Done");
						return 0;
					}
					Console.WriteLine($"Operation not started. FileId: {fileId}");
					return 1;
				} 
				Console.WriteLine($"File not uploaded. FileId: {fileId}");
				return 1;
			}
			catch (Exception e) {
				Console.WriteLine(e.Message);
				return 1;
			}
		}

		#endregion

	}

	#endregion

}