namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.Net;
	using System.Net.Http;
	using System.Net.Http.Headers;
	using System.Text;
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
		private const string ExpressionTypeKey = "expressionType";
		private const string ColumnPathKey = "columnPath";
		private const string ExpressionKey = "expression";

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

		/// <summary>
		/// Execute HTTP POST request with Basic Authentication.
		/// This bypasses CreatioClient's session management and uses fresh credentials on every request.
		/// </summary>
		private string ExecutePostWithBasicAuth(string url, string requestData, string login, string password, int timeoutMs = 100000) {
			using (var httpClient = new HttpClient()) {
				httpClient.Timeout = TimeSpan.FromMilliseconds(timeoutMs);
				
				string authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{login}:{password}"));
				httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
				
				var content = new StringContent(requestData, Encoding.UTF8, "application/json");
				
				HttpResponseMessage httpResponse;
				try {
					httpResponse = httpClient.PostAsync(url, content).Result;
				}
				catch (AggregateException ex) {
					throw new HttpRequestException($"HTTP request failed: {ex.InnerException?.Message ?? ex.Message}", ex.InnerException);
				}
				
				if (!httpResponse.IsSuccessStatusCode) {
					string errorBody = "";
					try {
						errorBody = httpResponse.Content.ReadAsStringAsync().Result;
					}
					catch { }
					
					throw new HttpRequestException(
						$"HTTP {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}. " +
						$"URL: {url}. " +
						(string.IsNullOrWhiteSpace(errorBody) ? "" : $"Body: {errorBody.Substring(0, Math.Min(200, errorBody.Length))}")
					);
				}
				
				string responseBody = httpResponse.Content.ReadAsStringAsync().Result;
				
				if (string.IsNullOrWhiteSpace(responseBody)) {
					throw new HttpRequestException($"Empty response body from {url}");
				}
				
				return responseBody;
			}
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
								["leftExpression"] = new JObject {[ExpressionTypeKey] = 0, [ColumnPathKey] = "Name"},
								["rightExpression"] = new JObject {[ExpressionTypeKey] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = options.SchemaName}}
							},
							["filter1"] = new JObject {
								["filterType"] = 1,
								["comparisonType"] = 3,
								["isEnabled"] = true,
								["trimDateTimeParameterToDate"] = false,
								["leftExpression"] = new JObject {[ExpressionTypeKey] = 0, [ColumnPathKey] = "ManagerName"},
								["rightExpression"] = new JObject {[ExpressionTypeKey] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = "ClientUnitSchemaManager"}}
							}
						}
					},
					["columns"] = new JObject {
						["items"] = new JObject {
							["Name"] = new JObject {
								[ExpressionKey] = new JObject {
									[ExpressionTypeKey] = 0,
									[ColumnPathKey] = "Name"
								}
							},
							["UId"] = new JObject {
								[ExpressionKey] = new JObject {
									[ExpressionTypeKey] = 0,
									[ColumnPathKey] = "UId"
								}
							},
							["PackageName"] = new JObject {
								[ExpressionKey] = new JObject {
									[ExpressionTypeKey] = 0,
									[ColumnPathKey] = "SysPackage.Name"
								}
							},
							["ParentSchemaName"] = new JObject {
								[ExpressionKey] = new JObject {
									[ExpressionTypeKey] = 0,
									[ColumnPathKey] = "[SysSchema:Id:Parent].Name"
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
				
				if (string.IsNullOrWhiteSpace(options.Login) || string.IsNullOrWhiteSpace(options.Password)) {
					response = new PageGetResponse { 
						Success = false, 
						Error = "Login and Password are required for page-get operation" 
					};
					return false;
				}
				
				string designerUrl = _serviceUrlBuilder.Build("/0/ServiceModel/ClientUnitSchemaDesignerService.svc/GetSchema");
				string bodyJson = ExecutePostWithBasicAuth(designerUrl, bodyRequest.ToString(Formatting.None), options.Login, options.Password);
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
			catch (HttpRequestException ex) {
				response = new PageGetResponse { Success = false, Error = $"HTTP error: {ex.Message}" };
				return false;
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
