namespace Clio.Command {
	using System;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	[Verb("page-get", HelpText = "Get Freedom UI page schema")]
	public class PageGetOptions : EnvironmentOptions {
		[Option("schema-name", Required = true, HelpText = "Schema name to fetch")]
		public string SchemaName { get; set; }
	}

	public class PageGetCommand : Command<PageGetOptions> {

		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;

		public PageGetCommand(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			ILogger logger) {
			_applicationClient = applicationClient;
			_serviceUrlBuilder = serviceUrlBuilder;
			_logger = logger;
		}

		public bool TryGetPage(PageGetOptions options, out PageGetResponse response) {
			try {
				var (metadata, error) = PageSchemaMetadataHelper.QuerySysSchemaRow(
					_applicationClient, _serviceUrlBuilder, options.SchemaName,
					("Name", "Name"), ("UId", "UId"),
					("PackageName", "SysPackage.Name"),
					("ParentSchemaName", "[SysSchema:Id:Parent].Name"));
				if (metadata == null) {
					response = new PageGetResponse { Success = false, Error = error };
					return false;
				}
				string schemaUId = metadata["UId"]?.ToString();
				var bodyRequest = new JObject {
					["schemaUId"] = schemaUId,
					["useFullHierarchy"] = false
				};
				string designerUrl = _serviceUrlBuilder.Build("/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
				string bodyJson = _applicationClient.ExecutePostRequest(designerUrl, bodyRequest.ToString(Formatting.None));
				var bodyResponse = JObject.Parse(bodyJson);
				if (!(bodyResponse["success"]?.Value<bool>() ?? false)) {
					response = new PageGetResponse { Success = false, Error = "Failed to load schema body" };
					return false;
				}
				string body = bodyResponse["schema"]?["body"]?.ToString();
				if (string.IsNullOrEmpty(body)) {
					response = new PageGetResponse { Success = false, Error = $"Schema '{options.SchemaName}' has no JS body" };
					return false;
				}
				response = new PageGetResponse {
					Success = true,
					SchemaName = metadata["Name"]?.ToString(),
					SchemaUId = schemaUId,
					PackageName = metadata["PackageName"]?.ToString(),
					ParentSchemaName = metadata["ParentSchemaName"]?.ToString(),
					Body = body
				};
				return true;
			}
			catch (Exception ex) {
				response = new PageGetResponse { Success = false, Error = ex.Message };
				return false;
			}
		}

		public override int Execute(PageGetOptions options) {
			bool success = TryGetPage(options, out PageGetResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}
	}
}
