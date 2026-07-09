namespace Clio.Command {
	using System.Linq;
	using Clio.Common;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	internal static class PageSchemaMetadataHelper {
		/// <summary>
		/// Canonical user-facing error for a syntactically invalid schema name. Shared by every
		/// call site that pairs with <see cref="IsValidSchemaName"/> so the message stays identical
		/// across the CLI and MCP surfaces (project-context.md: no hardcoded user-facing strings).
		/// </summary>
		internal const string SchemaNameFormatError =
			"schema-name must start with a letter and contain only letters, digits, or underscores";

		private const string ExpressionTypeKey = "expressionType";
		private const string ColumnPathKey = "columnPath";
		private const string SelectQueryUrl = "/DataService/json/SyncReply/SelectQuery";
		private const string FilterTypeKey = "filterType";
		private const string IsEnabledKey = "isEnabled";
		private const string ItemsKey = "items";
		private const string RootSchemaNameKey = "rootSchemaName";
		private const string OperationTypeKey = "operationType";
		private const string FiltersKey = "filters";
		private const string ColumnsKey = "columns";
		private const string RowCountKey = "rowCount";
		private const string ExpressionKey = "expression";
		private const string SysSchemaName = "SysSchema";
		private const string ManagerNameColumnPath = "ManagerName";
		private const string ClientUnitSchemaManagerName = "ClientUnitSchemaManager";
		private const int ComparisonTypeEqual = 3;

		private static (JArray rows, bool success) ExecuteSelectQuery(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			JObject query) {
			try {
				string url = serviceUrlBuilder.Build(SelectQueryUrl);
				string responseJson = applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
				JObject response = JObject.Parse(responseJson);
				bool ok = response["success"]?.Value<bool>() ?? false;
				return ok ? (response["rows"] as JArray ?? new JArray(), true) : (new JArray(), false);
			} catch {
				return (new JArray(), false);
			}
		}

		private static JObject BuildComparisonFilter(string columnPath, int comparisonType, int dataValueType, JToken value) =>
			new JObject {
				[FilterTypeKey] = 1, ["comparisonType"] = comparisonType, [IsEnabledKey] = true,
				["leftExpression"] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = columnPath },
				["rightExpression"] = new JObject { [ExpressionTypeKey] = 2,
					["parameter"] = new JObject { ["dataValueType"] = dataValueType, ["value"] = value } }
			};

		private static JObject BuildEqFilter(string columnPath, int dataValueType, JToken value) =>
			BuildComparisonFilter(columnPath, ComparisonTypeEqual, dataValueType, value);

		private static JObject BuildFilterGroup(params (string key, JObject filter)[] filters) {
			var items = new JObject();
			foreach ((string key, JObject filter) in filters)
				items[key] = filter;
			return new JObject {
				[FilterTypeKey] = 6, ["logicalOperation"] = 0, [IsEnabledKey] = true,
				[ItemsKey] = items
			};
		}

		private static JObject BuildUIdColumnSelection() =>
			new JObject {
				[ItemsKey] = new JObject {
					["UId"] = new JObject {
						[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "UId" }
					}
				}
			};

		internal static bool IsValidSchemaName(string name) {
			if (string.IsNullOrEmpty(name) || !char.IsLetter(name[0]))
				return false;
			return name.All(c => char.IsLetterOrDigit(c) || c == '_');
		}

		internal static bool SchemaNameExists(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string schemaName) {
			(JToken row, _) = QuerySysSchemaRow(applicationClient, serviceUrlBuilder, schemaName, ("UId", "UId"));
			return row != null;
		}

		internal static string FindExistingSchemaInPackage(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string schemaName,
			string packageUId) {
			var (uId, _) = QueryExistingSchemaInPackage(
				applicationClient,
				serviceUrlBuilder,
				schemaName,
				packageUId);
			return uId;
		}

		internal static (string uId, string error) QueryExistingSchemaInPackage(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string schemaName,
			string packageUId) {
			if (string.IsNullOrWhiteSpace(schemaName) || string.IsNullOrWhiteSpace(packageUId))
				return (null, null);
			var query = new JObject {
				[RootSchemaNameKey] = SysSchemaName, [OperationTypeKey] = 0,
				[FiltersKey] = BuildFilterGroup(
					("byName", BuildEqFilter("Name", 1, schemaName)),
					("byManager", BuildEqFilter(ManagerNameColumnPath, 1, ClientUnitSchemaManagerName)),
					("byPackage", BuildEqFilter("SysPackage.UId", 0, packageUId))),
				[ColumnsKey] = BuildUIdColumnSelection(),
				[RowCountKey] = 1
			};
			var (rows, success) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (!success)
				return (null, "Failed to query schema metadata in target package.");
			return (rows.Count > 0 ? rows[0]?["UId"]?.ToString() : null, null);
		}

		internal static string QueryPackageName(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string packageUId) {
			if (string.IsNullOrWhiteSpace(packageUId))
				return null;
			var query = new JObject {
				[RootSchemaNameKey] = "SysPackage", [OperationTypeKey] = 0,
				[FiltersKey] = BuildFilterGroup(("byUId", BuildEqFilter("UId", 0, packageUId))),
				[ColumnsKey] = new JObject {
					[ItemsKey] = new JObject {
						["Name"] = new JObject { [ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "Name" } }
					}
				},
				[RowCountKey] = 1
			};
			var (rows, _) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			return rows.Count > 0 ? rows[0]?["Name"]?.ToString() : null;
		}

		internal static (string uId, string error) QueryPackageUId(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string packageName) {
			var query = new JObject {
				[RootSchemaNameKey] = "SysPackage", [OperationTypeKey] = 0,
				[FiltersKey] = BuildFilterGroup(("byName", BuildEqFilter("Name", 1, packageName))),
				[ColumnsKey] = BuildUIdColumnSelection(),
				[RowCountKey] = 1
			};
			var (rows, success) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (!success)
				return (null, "Failed to query SysPackage");
			if (rows.Count == 0)
				return (null, $"Package '{packageName}' not found in the target environment.");
			string uId = rows[0]["UId"]?.ToString();
			if (string.IsNullOrWhiteSpace(uId))
				return (null, $"Package '{packageName}' has no UId in the SysPackage response.");
			return (uId, null);
		}

		/// <summary>
		/// Resolves a page (client-unit) schema <c>UId</c> back to its <c>Name</c> via the DataService
		/// SelectQuery endpoint. Used by <c>get-related-page-addon</c> to surface friendly page names for the
		/// UIds stored in the RelatedPage add-on metadata. Returns <c>null</c> when the UId is empty or the
		/// schema is not found. (The forward name-to-UId resolution the write path needs is package- and
		/// replacement-aware and lives in <see cref="PageSchemaResolver"/>, not here.)
		/// </summary>
		/// <remarks>
		/// Like the other <c>SysSchema</c> lookups in this helper, schema resolution intentionally uses the
		/// DataService <c>SelectQuery</c> over <c>SysSchema</c> rather than a ClioGate endpoint — the established,
		/// repo-consistent pattern (the same primitive backs <c>create-page-business-rules</c> and
		/// <c>create-page</c>), none of which introduce a ClioGate dependency (reserved for privileged
		/// write/elevated operations). Trade-off: the caller needs DataService read access to <c>SysSchema</c>
		/// (a full schema-management user); a restricted solution-management user without that access would get a
		/// SecurityException. This is a pre-existing, repo-wide limitation, accepted for consistency.
		/// </remarks>
		internal static string QueryPageSchemaNameByUId(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string pageSchemaUId) {
			if (string.IsNullOrWhiteSpace(pageSchemaUId)) {
				return null;
			}
			(JToken row, _) = QuerySysSchemaRowByUId(applicationClient, serviceUrlBuilder, pageSchemaUId, ("Name", "Name"));
			return row?["Name"]?.ToString();
		}

		internal static (string uId, string error) QueryEntitySchemaUId(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string entitySchemaName) {
			var query = new JObject {
				[RootSchemaNameKey] = SysSchemaName, [OperationTypeKey] = 0,
				[FiltersKey] = BuildFilterGroup(
					("byName", BuildEqFilter("Name", 1, entitySchemaName)),
					("byManager", BuildEqFilter(ManagerNameColumnPath, 1, "EntitySchemaManager"))),
				[ColumnsKey] = BuildUIdColumnSelection(),
				[RowCountKey] = 1
			};
			var (rows, success) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (!success)
				return (null, "Failed to query entity schema metadata");
			if (rows.Count == 0)
				return (null, $"Entity schema '{entitySchemaName}' not found.");
			return (rows[0]["UId"]?.ToString(), null);
		}

		internal static string ParseSaveErrorMessage(JObject saveResponse, string defaultMessage) {
			string errorMessage = defaultMessage;
			if (saveResponse["errorInfo"] is JObject errorInfo) {
				string infoMessage = errorInfo["message"]?.ToString();
				if (!string.IsNullOrWhiteSpace(infoMessage))
					errorMessage = infoMessage;
			}
			if (saveResponse["validationErrors"] is JArray validationErrors && validationErrors.Count > 0) {
				System.Collections.Generic.IEnumerable<string> messages = validationErrors
					.Select(e => e["message"]?.ToString() ?? e["caption"]?.ToString())
					.Where(m => !string.IsNullOrWhiteSpace(m));
				errorMessage = string.Join("; ", messages);
			}
			if (saveResponse["addonsErrors"] is JArray addonsErrors && addonsErrors.Count > 0)
				errorMessage = string.Join("; ", addonsErrors.Select(e => e.ToString()));
			return errorMessage;
		}

		/// <summary>
		/// Queries a single <c>SysSchema</c> row by schema <c>UId</c> via the DataService SelectQuery endpoint.
		/// </summary>
		/// <param name="applicationClient">Authenticated Creatio HTTP client.</param>
		/// <param name="serviceUrlBuilder">Environment-aware URL builder.</param>
		/// <param name="schemaUId">Schema identifier (GUID string) used as the filter value.</param>
		/// <param name="columns">Column projections as (alias, columnPath) pairs, e.g. ("Checksum", "Checksum").</param>
		/// <returns>The first matching row, or <c>null</c> with a non-empty error when the query fails or no row matches.</returns>
		internal static (JToken row, string error) QuerySysSchemaRowByUId(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string schemaUId,
			params (string alias, string path)[] columns) {
			var columnsItems = new JObject();
			foreach ((string alias, string path) in columns) {
				columnsItems[alias] = new JObject {
					[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = path }
				};
			}
			var query = new JObject {
				[RootSchemaNameKey] = SysSchemaName, [OperationTypeKey] = 0,
				[FiltersKey] = BuildFilterGroup(
					("byUId", BuildEqFilter("UId", 0, schemaUId)),
					("byManager", BuildEqFilter(ManagerNameColumnPath, 1, ClientUnitSchemaManagerName))),
				[ColumnsKey] = new JObject { [ItemsKey] = columnsItems },
				[RowCountKey] = 1
			};
			var (rows, success) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (!success)
				return (null, "Failed to query schema metadata");
			if (rows.Count == 0)
				return (null, $"Schema '{schemaUId}' not found");
			return (rows[0], null);
		}

		internal static (JToken row, string error) QuerySysSchemaRow(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string schemaName,
			params (string alias, string path)[] columns) {
			var columnsItems = new JObject();
			foreach ((string alias, string path) in columns) {
				columnsItems[alias] = new JObject {
					[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = path }
				};
			}
			var query = new JObject {
				[RootSchemaNameKey] = SysSchemaName, [OperationTypeKey] = 0,
				[FiltersKey] = new JObject {
					[FilterTypeKey] = 6, ["logicalOperation"] = 0, [IsEnabledKey] = true,
					["trimDateTimeParameterToDate"] = false,
					[ItemsKey] = new JObject {
						["filter0"] = new JObject {
							[FilterTypeKey] = 1, ["comparisonType"] = 3, [IsEnabledKey] = true,
							["trimDateTimeParameterToDate"] = false,
							["leftExpression"] = new JObject {[ExpressionTypeKey] = 0, [ColumnPathKey] = "Name"},
							["rightExpression"] = new JObject {[ExpressionTypeKey] = 2,
								["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = schemaName}}
						},
						["filter1"] = new JObject {
							[FilterTypeKey] = 1, ["comparisonType"] = 3, [IsEnabledKey] = true,
							["trimDateTimeParameterToDate"] = false,
							["leftExpression"] = new JObject {[ExpressionTypeKey] = 0, [ColumnPathKey] = ManagerNameColumnPath},
							["rightExpression"] = new JObject {[ExpressionTypeKey] = 2,
								["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = ClientUnitSchemaManagerName}}
						}
					}
				},
				[ColumnsKey] = new JObject { [ItemsKey] = columnsItems },
				[RowCountKey] = 1
			};
			// Route through the shared, guarded ExecuteSelectQuery (like QuerySysSchemaRowByUId and every other
			// lookup in this helper) instead of a raw ExecutePostRequest + JObject.Parse: an expired session that
			// returns an HTML/redirect body then surfaces as a clean lookup failure rather than a raw
			// "Unexpected character '<'" JSON parse exception.
			var (rows, success) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (!success)
				return (null, "Failed to query schema metadata");
			if (rows.Count == 0)
				return (null, $"Schema '{schemaName}' not found");
			return (rows[0], null);
		}
	}
}
