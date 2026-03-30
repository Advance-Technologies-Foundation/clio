namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	[Verb("page-list", HelpText = "List Freedom UI pages")]
	public class PageListOptions : EnvironmentOptions {
		[Option("package-name", Required = false, HelpText = "Filter by package name")]
		public string PackageName { get; set; }

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
				var filters = new JObject {
					[FilterTypeKey] = 6,
					[ItemsKey] = new JObject {
						["ManagerName"] = new JObject {
							[FilterTypeKey] = 1,
							["comparisonType"] = 3,
							["leftExpression"] = new JObject {[ExpressionTypeKey] = 0, [ColumnPathKey] = "ManagerName"},
							["rightExpression"] = new JObject {[ExpressionTypeKey] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = "ClientUnitSchemaManager"}}
						}
					}
				};
				if (!string.IsNullOrWhiteSpace(options.PackageName)) {
					filters[ItemsKey]["PackageName"] = new JObject {
						[FilterTypeKey] = 1,
						["comparisonType"] = 3,
						["leftExpression"] = new JObject {[ExpressionTypeKey] = 0, [ColumnPathKey] = "SysPackage.Name"},
						["rightExpression"] = new JObject {[ExpressionTypeKey] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = options.PackageName}}
					};
				}
				if (!string.IsNullOrWhiteSpace(options.SearchPattern)) {
					filters[ItemsKey]["Name"] = new JObject {
						[FilterTypeKey] = 1,
						["comparisonType"] = 11,
						["leftExpression"] = new JObject {[ExpressionTypeKey] = 0, [ColumnPathKey] = "Name"},
						["rightExpression"] = new JObject {[ExpressionTypeKey] = 2, ["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = options.SearchPattern}}
					};
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
						Name = row["Name"]?.ToString(),
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

		public override int Execute(PageListOptions options) {
			bool success = TryListPages(options, out PageListResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}
	}
}
