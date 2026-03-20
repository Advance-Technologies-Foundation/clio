namespace Clio.Command {
	using System;
	using System.Collections.Generic;
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
				var metadataQuery = new JObject {
					["rootSchemaName"] = "SysSchema",
					["operationType"] = 0,
					["filters"] = new JObject {
						["filterType"] = 6,
						["logicalOperation"] = 0,
						["isEnabled"] = true,
						["trimDateTimeParameterToDate"] = false,
						["items"] = new JObject {
							["filter0"] = new JObject {
								["filterType"] = 1,
								["comparisonType"] = 3,
								["isEnabled"] = true,
								["trimDateTimeParameterToDate"] = false,
								["leftExpression"] = new JObject {["expressionType"] = 0, ["columnPath"] = "Name"},
								["rightExpression"] = new JObject {["expressionType"] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = options.SchemaName}}
							},
							["filter1"] = new JObject {
								["filterType"] = 1,
								["comparisonType"] = 3,
								["isEnabled"] = true,
								["trimDateTimeParameterToDate"] = false,
								["leftExpression"] = new JObject {["expressionType"] = 0, ["columnPath"] = "ManagerName"},
								["rightExpression"] = new JObject {["expressionType"] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = "ClientUnitSchemaManager"}}
							}
						}
					},
					["columns"] = new JObject {
						["items"] = new JObject {
							["Name"] = new JObject {
								["expression"] = new JObject {
									["expressionType"] = 0,
									["columnPath"] = "Name"
								}
							},
							["UId"] = new JObject {
								["expression"] = new JObject {
									["expressionType"] = 0,
									["columnPath"] = "UId"
								}
							},
							["PackageName"] = new JObject {
								["expression"] = new JObject {
									["expressionType"] = 0,
									["columnPath"] = "SysPackage.Name"
								}
							},
							["ParentSchemaName"] = new JObject {
								["expression"] = new JObject {
									["expressionType"] = 0,
									["columnPath"] = "[SysSchema:Id:Parent].Name"
								}
							}
						}
					},
					["rowCount"] = 1
				};
				string dataServiceUrl = _serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery");
				string metadataJson = _applicationClient.ExecutePostRequest(dataServiceUrl, metadataQuery.ToString(Formatting.None));
				var metadataResponse = JObject.Parse(metadataJson);
				if (!(metadataResponse["success"]?.Value<bool>() ?? false)) {
					response = new PageGetResponse { Success = false, Error = "Failed to query schema metadata" };
					return false;
				}
				var rows = metadataResponse["rows"] as JArray ?? new JArray();
				if (rows.Count == 0) {
					response = new PageGetResponse { Success = false, Error = $"Schema '{options.SchemaName}' not found" };
					return false;
				}
				var metadata = rows[0];
				string schemaUId = metadata["UId"]?.ToString();
				var bodyRequest = new JObject {
					["schemaUId"] = schemaUId,
					["useFullHierarchy"] = false
				};
				string designerUrl = _serviceUrlBuilder.Build("/0/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
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
