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

		[Option("uid", Required = false, HelpText = "Filter by schema UId (exact match)")]
		public string UId { get; set; }
	}

	public class PageListCommand : Command<PageListOptions> {
		private const string ExpressionTypeKey = "expressionType";
		private const string ColumnPathKey = "columnPath";
		private const string FilterTypeKey = "filterType";
		private const string ItemsKey = "items";
		private const string ExpressionKey = "expression";
		private const string SuccessKey = "success";
		private const int ContainsComparisonType = 11;
		private const int EmptyFilterFallbackRowCount = 10000;

		/// <summary>
		/// Result cap applied when <c>limit</c> is omitted or supplied as 0 ("use the default").
		/// </summary>
		internal const int DefaultLimit = 50;

		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;

		private sealed record PageQueryContext(
			string Url,
			string PackageName,
			string NameFilter,
			string UId,
			int EffectiveLimit);

		private sealed record PageFallbackResult(
			List<PageListItem> Pages,
			int Total,
			bool MayBeCapped);

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
				if (!TryCreateQueryContext(options, out PageQueryContext context, out response)) {
					return false;
				}
				List<PageListItem>? queriedPages = TryQueryPages(
					context.Url,
					context.PackageName,
					context.NameFilter,
					context.UId,
					context.EffectiveLimit);
				if (queriedPages is null) {
					response = new PageListResponse { Success = false, Error = "Query failed" };
					return false;
				}
				List<PageListItem> pages = queriedPages;
				PageFallbackResult? fallback = null;
				if (pages.Count == 0 && !string.IsNullOrWhiteSpace(context.PackageName)) {
					fallback = CrossCheckEmptyPackagePages(context);
					if (fallback is null) {
						response = new PageListResponse {
							Success = false,
							Error = "The package-filtered query returned no rows, and the broader verification query failed."
						};
						return false;
					}
					pages = fallback.Pages;
				}
				response = CreateSuccessResponse(pages, context, fallback);
				return true;
			}
			catch (Exception ex) {
				response = new PageListResponse { Success = false, Error = ex.Message };
				return false;
			}
		}

		private bool TryCreateQueryContext(
			PageListOptions options,
			out PageQueryContext context,
			out PageListResponse response) {
			context = null;
			if (!string.IsNullOrWhiteSpace(options.PackageName) && !string.IsNullOrWhiteSpace(options.AppCode)) {
				response = new PageListResponse {
					Success = false,
					Error = "Provide either package-name or app-code, not both."
				};
				return false;
			}
			// A negative limit must NOT silently disable the result cap (Creatio treats a
			// negative rowCount as "no limit", which would return every page on the
			// environment). Reject it; treat 0 as "use the default".
			if (options.Limit < 0) {
				response = new PageListResponse {
					Success = false,
					Error = $"limit must be zero or greater (got {options.Limit}). Omit limit or pass 0 to use the default of {DefaultLimit}."
				};
				return false;
			}
			string packageName = options.PackageName;
			if (string.IsNullOrWhiteSpace(packageName) && !string.IsNullOrWhiteSpace(options.AppCode)) {
				packageName = ResolvePrimaryPackageName(options.AppCode);
			}
			context = new PageQueryContext(
				_serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery"),
				packageName,
				options.SearchPattern?.Trim('*', ' ') ?? string.Empty,
				options.UId,
				options.Limit == 0 ? DefaultLimit : options.Limit);
			response = null;
			return true;
		}

		private PageFallbackResult? CrossCheckEmptyPackagePages(PageQueryContext context) {
			// Issue #1213: a freshly-created package returned no rows through the package-name
			// relation filter while the same rows were already visible through an unscoped read.
			// Cross-check an empty result once and filter the returned package names locally before
			// treating the package as absent. The normal filtered query remains the fast path.
			List<PageListItem>? broaderPages;
			try {
				broaderPages = TryQueryPages(
					context.Url,
					packageName: string.Empty,
					context.NameFilter,
					context.UId,
					EmptyFilterFallbackRowCount);
			}
			catch (Exception exception) when (exception is System.Net.Http.HttpRequestException
				or System.Net.WebException
				or TimeoutException
				or Newtonsoft.Json.JsonException) {
				broaderPages = null;
			}
			if (broaderPages is null) {
				return null;
			}
			List<PageListItem> packageMatches = broaderPages
				.Where(page => string.Equals(
					page.PackageName,
					context.PackageName,
					StringComparison.OrdinalIgnoreCase))
				.ToList();
			return new PageFallbackResult(
				packageMatches.Take(context.EffectiveLimit).ToList(),
				packageMatches.Count,
				broaderPages.Count >= EmptyFilterFallbackRowCount);
		}

		private PageListResponse CreateSuccessResponse(
			List<PageListItem> pages,
			PageQueryContext context,
			PageFallbackResult? fallback) {
			// The capped data query cannot reveal how many pages matched in total, so a caller
			// could not otherwise tell a 50-item page from a complete result. Only when the page
			// is provably full (count >= the cap) is a separate COUNT(Id) round-trip worth its
			// cost: a short page (count < cap) is already complete, so issuing the count there is
			// pure waste. When the page IS capped and the count query fails, we cannot prove
			// completeness, so the result must be reported as truncated.
			if (fallback is not null) {
				return CreateSuccessResponse(pages, fallback.Total, fallback.MayBeCapped || fallback.Total > pages.Count);
			}
			if (pages.Count < context.EffectiveLimit) {
				return CreateSuccessResponse(pages, pages.Count, truncated: false);
			}
			(bool countSucceeded, int countTotal) = QueryTotalPageCount(
				context.Url,
				context.PackageName,
				context.NameFilter,
				context.UId,
				pages.Count);
			int total = countSucceeded ? Math.Max(countTotal, pages.Count) : pages.Count;
			return CreateSuccessResponse(pages, total, !countSucceeded || total > pages.Count);
		}

		private static PageListResponse CreateSuccessResponse(
			List<PageListItem> pages,
			int total,
			bool truncated) {
			return new PageListResponse {
				Success = true,
				Count = pages.Count,
				Total = total,
				Truncated = truncated,
				Pages = pages
			};
		}

		private static JObject BuildPageFilters(string packageName, string nameFilter, string uId) {
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
			if (!string.IsNullOrWhiteSpace(nameFilter)) {
				filters[ItemsKey]["Name"] = BuildComparisonFilter("Name", nameFilter, 1, ContainsComparisonType);
			}
			if (!string.IsNullOrWhiteSpace(uId)) {
				filters[ItemsKey]["UId"] = BuildComparisonFilter("UId", uId, 0, 3);
			}
			return filters;
		}

		private List<PageListItem>? TryQueryPages(
			string url,
			string packageName,
			string nameFilter,
			string uId,
			int rowCount) {
			var query = new JObject {
				["rootSchemaName"] = "SysSchema",
				["operationType"] = 0,
				["filters"] = BuildPageFilters(packageName, nameFilter, uId),
				["columns"] = new JObject {
					[ItemsKey] = BuildPageColumns()
				},
				["rowCount"] = rowCount
			};
			string responseJson = _applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
			var rawResponse = JObject.Parse(responseJson);
			if (!(rawResponse[SuccessKey]?.Value<bool>() ?? false)) {
				return null;
			}
			if (rawResponse["rows"] is not JArray rows) {
				return null;
			}
			return rows
				.Select(MapPage)
				.ToList();
		}

		private static JObject BuildPageColumns() {
			return new JObject {
				["Name"] = BuildPageColumn("Name"),
				["UId"] = BuildPageColumn("UId"),
				["PackageName"] = BuildPageColumn("SysPackage.Name"),
				["ParentSchemaName"] = BuildPageColumn("[SysSchema:Id:Parent].Name")
			};
		}

		private static JObject BuildPageColumn(string columnPath) {
			return new JObject {
				[ExpressionKey] = new JObject {
					[ExpressionTypeKey] = 0,
					[ColumnPathKey] = columnPath
				},
				["orderDirection"] = 0,
				["orderPosition"] = -1,
				["isVisible"] = true
			};
		}

		private static PageListItem MapPage(JToken row) {
			return new PageListItem {
				SchemaName = row["Name"]?.ToString(),
				UId = row["UId"]?.ToString(),
				PackageName = row["PackageName"]?.ToString(),
				ParentSchemaName = row["ParentSchemaName"]?.ToString()
			};
		}

		/// <summary>
		/// Queries the full number of pages matching the same filters as the data query (ignoring
		/// the result cap) via a COUNT(Id) aggregation. Returns whether the count was obtained and the
		/// resulting total. When the count query fails or returns an unexpected shape, returns
		/// <c>Succeeded:false</c> with <paramref name="returnedCount"/> so the caller can decide how to
		/// surface the unprovable case (a capped page becomes truncated) instead of silently claiming
		/// the result is complete.
		/// </summary>
		private (bool Succeeded, int Total) QueryTotalPageCount(string url, string packageName, string nameFilter, string uId, int returnedCount) {
			try {
				var countQuery = new JObject {
					["rootSchemaName"] = "SysSchema",
					["operationType"] = 0,
					["filters"] = BuildPageFilters(packageName, nameFilter, uId),
					["columns"] = new JObject {
						[ItemsKey] = new JObject {
							["RecordCount"] = new JObject {
								[ExpressionKey] = new JObject {
									[ExpressionTypeKey] = 1,
									["functionType"] = 2,
									["aggregationType"] = 1,
									["functionArgument"] = new JObject {
										[ExpressionTypeKey] = 0,
										[ColumnPathKey] = "Id"
									}
								}
							}
						}
					}
				};
				string countResponseJson = _applicationClient.ExecutePostRequest(url, countQuery.ToString(Formatting.None));
				var countResponse = JObject.Parse(countResponseJson);
				if (!(countResponse[SuccessKey]?.Value<bool>() ?? false)) {
					return (false, returnedCount);
				}
				JToken recordCount = (countResponse["rows"] as JArray)?.FirstOrDefault()?["RecordCount"];
				if (recordCount is not null && int.TryParse(recordCount.ToString(), out int total)) {
					return (true, Math.Max(total, returnedCount));
				}
				return (false, returnedCount);
			}
			catch (Newtonsoft.Json.JsonException) {
				// A malformed count body is informational only; never fail the whole listing because
				// the supplementary count could not be parsed.
				return (false, returnedCount);
			}
		}

		private string ResolvePrimaryPackageName(string appCode) {
			string selectUrl = _serviceUrlBuilder.Build("/DataService/json/SyncReply/SelectQuery");
			string applicationResponseJson = _applicationClient.ExecutePostRequest(
				selectUrl,
				BuildInstalledApplicationQuery(appCode).ToString(Formatting.None));
			var applicationResponse = JObject.Parse(applicationResponseJson);
			if (!(applicationResponse[SuccessKey]?.Value<bool>() ?? false)) {
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
			if (!(packagesResponse[SuccessKey]?.Value<bool>() ?? false)) {
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
