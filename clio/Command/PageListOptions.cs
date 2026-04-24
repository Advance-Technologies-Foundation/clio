namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	[Verb("list-pages", Aliases = ["page-list"], HelpText = "List Freedom UI pages")]
	public class PageListOptions : EnvironmentOptions {
		[Option("package-name", Required = false, HelpText = "Filter by package name")]
		public string PackageName { get; set; }

		[Option("app-code", Required = false, HelpText = "Filter by installed application code using its primary package")]
		public string AppCode { get; set; }

		[Option("search-pattern", Required = false, HelpText = "Filter by schema name (contains)")]
		public string SearchPattern { get; set; }

		[Option("limit", Required = false, Default = 50, HelpText = "Maximum number of results")]
		public int Limit { get; set; }
	}

	public class PageListCommand : Command<PageListOptions> {
		private const string ExpressionTypeKey = "expressionType";
		private const string ColumnPathKey = "columnPath";
		private const string FilterTypeKey = "filterType";
		private const string ItemsKey = "items";
		private const string ExpressionKey = "expression";
		private const int ContainsComparisonType = 10;

		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;

		public PageListCommand(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			ILogger logger) {
			_applicationClient = applicationClient;
			_serviceUrlBuilder = serviceUrlBuilder;
			_logger = logger;
		}

		public bool TryListPages(PageListOptions options, out PageListResponse response) {
			try {
				if (!string.IsNullOrWhiteSpace(options.PackageName) && !string.IsNullOrWhiteSpace(options.AppCode)) {
					response = new PageListResponse {
						Success = false,
						Error = "Provide either package-name or app-code, not both."
					};
					return false;
				}
				string packageName = options.PackageName;
				if (string.IsNullOrWhiteSpace(packageName) && !string.IsNullOrWhiteSpace(options.AppCode)) {
					packageName = ResolvePrimaryPackageName(options.AppCode);
				}
				var filters = new JObject {
					[FilterTypeKey] = 6,
					["logicalOperation"] = 0,
					["isEnabled"] = true,
					[ItemsKey] = new JObject {
						["ManagerName"] = BuildComparisonFilter("ManagerName", "ClientUnitSchemaManager", 1, 3)
					}
				};
				if (!string.IsNullOrWhiteSpace(packageName)) {
					filters[ItemsKey]["PackageName"] = BuildComparisonFilter("SysPackage.Name", packageName, 1, 3);
				}
				string nameFilter = options.SearchPattern?.Trim('*', ' ') ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(nameFilter)) {
					filters[ItemsKey]["Name"] = BuildComparisonFilter("Name", nameFilter, 1, ContainsComparisonType);
				}
				var selectQuery = new JObject {
					["rootSchemaName"] = "SysSchema",
					["operationType"] = 0,
					["filters"] = filters,
					["columns"] = new JObject {
						[ItemsKey] = new JObject {
							["Name"] = new JObject {
								[ExpressionKey] = new JObject {
									[ExpressionTypeKey] = 0,
									[ColumnPathKey] = "Name"
								},
								["orderDirection"] = 0,
								["orderPosition"] = -1,
								["isVisible"] = true
							},
							["UId"] = new JObject {
								[ExpressionKey] = new JObject {
									[ExpressionTypeKey] = 0,
									[ColumnPathKey] = "UId"
								},
								["orderDirection"] = 0,
								["orderPosition"] = -1,
								["isVisible"] = true
							},
							["PackageName"] = new JObject {
								[ExpressionKey] = new JObject {
									[ExpressionTypeKey] = 0,
									[ColumnPathKey] = "SysPackage.Name"
								},
								["orderDirection"] = 0,
								["orderPosition"] = -1,
								["isVisible"] = true
							},
							["ParentSchemaName"] = new JObject {
								[ExpressionKey] = new JObject {
									[ExpressionTypeKey] = 0,
									[ColumnPathKey] = "[SysSchema:Id:Parent].Name"
								},
								["orderDirection"] = 0,
								["orderPosition"] = -1,
								["isVisible"] = true
							}
						}
					},
					["rowCount"] = options.Limit
				};
				string url = _serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery");
				string requestBody = selectQuery.ToString(Formatting.None);
				string responseJson = _applicationClient.ExecutePostRequest(url, requestBody);
				var rawResponse = JObject.Parse(responseJson);
				if (!(rawResponse["success"]?.Value<bool>() ?? false)) {
					response = new PageListResponse { Success = false, Error = "Query failed" };
					return false;
				}
				var rows = rawResponse["rows"] as JArray ?? new JArray();
				var pages = new List<PageListItem>();
				foreach (var row in rows) {
					pages.Add(new PageListItem {
						SchemaName = row["Name"]?.ToString(),
						UId = row["UId"]?.ToString(),
						PackageName = row["PackageName"]?.ToString(),
						ParentSchemaName = row["ParentSchemaName"]?.ToString()
					});
				}
				response = new PageListResponse {
					Success = true,
					Count = pages.Count,
					Pages = pages
				};
				return true;
			}
			catch (Exception ex) {
				response = new PageListResponse { Success = false, Error = ex.Message };
				return false;
			}
		}

		private string ResolvePrimaryPackageName(string appCode) {
			string selectUrl = _serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery");
			string applicationResponseJson = _applicationClient.ExecutePostRequest(
				selectUrl,
				BuildInstalledApplicationQuery(appCode).ToString(Formatting.None));
			var applicationResponse = JObject.Parse(applicationResponseJson);
			if (!(applicationResponse["success"]?.Value<bool>() ?? false)) {
				throw new InvalidOperationException("Application lookup failed.");
			}
			var applicationRow = (applicationResponse["rows"] as JArray)?.FirstOrDefault() as JObject;
			if (applicationRow is null) {
				throw new InvalidOperationException($"Application '{appCode}' not found.");
			}
			string applicationId = applicationRow["Id"]?.ToString();
			if (string.IsNullOrWhiteSpace(applicationId)) {
				throw new InvalidOperationException($"Application '{appCode}' did not return an id.");
			}
			string packagesResponseJson = _applicationClient.ExecutePostRequest(
				_serviceUrlBuilder.Build("ServiceModel/ApplicationPackagesService.svc/GetApplicationPackages"),
				JsonConvert.SerializeObject(applicationId));
			var packagesResponse = JObject.Parse(packagesResponseJson);
			if (!(packagesResponse["success"]?.Value<bool>() ?? false)) {
				throw new InvalidOperationException(packagesResponse["errorInfo"]?["message"]?.ToString() ?? "Failed to load application packages.");
			}
			var primaryPackage = (packagesResponse["packages"] as JArray)?
				.OfType<JObject>()
				.FirstOrDefault(package => package["isApplicationPrimaryPackage"]?.Value<bool>() == true);
			string packageName = primaryPackage?["name"]?.ToString();
			if (string.IsNullOrWhiteSpace(packageName)) {
				throw new InvalidOperationException($"Primary package was not found for application '{appCode}'.");
			}
			return packageName;
		}

		private static JObject BuildInstalledApplicationQuery(string appCode) {
			return new JObject {
				["rootSchemaName"] = "SysInstalledApp",
				["operationType"] = 0,
				["filters"] = new JObject {
					[FilterTypeKey] = 6,
					[ItemsKey] = new JObject {
						["Code"] = BuildComparisonFilter("Code", appCode, 1, 3)
					}
				},
				["columns"] = new JObject {
					[ItemsKey] = new JObject {
						["Id"] = new JObject {
							[ExpressionKey] = new JObject {
								[ExpressionTypeKey] = 0,
								[ColumnPathKey] = "Id"
							},
							["orderDirection"] = 0,
							["orderPosition"] = -1,
							["isVisible"] = true
						}
					}
				},
				["rowCount"] = 1
			};
		}

		private static JObject BuildComparisonFilter(string columnPath, string value, int dataValueType, int comparisonType) {
			return new JObject {
				[FilterTypeKey] = 1,
				["comparisonType"] = comparisonType,
				["leftExpression"] = new JObject {[ExpressionTypeKey] = 0, [ColumnPathKey] = columnPath},
				["rightExpression"] = new JObject {[ExpressionTypeKey] = 2, ["parameter"] = new JObject {["dataValueType"] = dataValueType, ["value"] = value}}
			};
		}

		public override int Execute(PageListOptions options) {
			bool success = TryListPages(options, out PageListResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}
	}
}
