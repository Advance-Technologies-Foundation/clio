namespace Clio.Command {
	using System.Linq;
	using Clio.Common;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	internal static class PageSchemaMetadataHelper {
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

		private static JArray ExecuteSelectQuery(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			JObject query) {
			try {
				string url = serviceUrlBuilder.Build(SelectQueryUrl);
				string responseJson = applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
				JObject response = JObject.Parse(responseJson);
				return (response["success"]?.Value<bool>() ?? false)
					? response["rows"] as JArray ?? new JArray()
					: null;
			} catch {
				return null;
			}
		}

		private static JObject BuildEqFilter(string columnPath, int dataValueType, JToken value) =>
			new JObject {
				[FilterTypeKey] = 1, ["comparisonType"] = 3, [IsEnabledKey] = true,
				["leftExpression"] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = columnPath },
				["rightExpression"] = new JObject { [ExpressionTypeKey] = 2,
					["parameter"] = new JObject { ["dataValueType"] = dataValueType, ["value"] = value } }
			};

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
						["expression"] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "UId" }
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
			if (string.IsNullOrWhiteSpace(schemaName) || string.IsNullOrWhiteSpace(packageUId))
				return null;
			var query = new JObject {
				[RootSchemaNameKey] = "SysSchema", [OperationTypeKey] = 0,
				[FiltersKey] = BuildFilterGroup(
					("byName", BuildEqFilter("Name", 1, schemaName)),
					("byManager", BuildEqFilter("ManagerName", 1, "ClientUnitSchemaManager")),
					("byPackage", BuildEqFilter("SysPackage.UId", 0, packageUId))),
				[ColumnsKey] = BuildUIdColumnSelection(),
				[RowCountKey] = 1
			};
			var rows = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			return rows?.Count > 0 ? rows[0]?["UId"]?.ToString() : null;
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
						["Name"] = new JObject { ["expression"] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "Name" } }
					}
				},
				[RowCountKey] = 1
			};
			var rows = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			return rows?.Count > 0 ? rows[0]?["Name"]?.ToString() : null;
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
			var rows = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (rows is null)
				return (null, "Failed to query SysPackage");
			if (rows.Count == 0)
				return (null, $"Package '{packageName}' not found in the target environment.");
			string uId = rows[0]["UId"]?.ToString();
			if (string.IsNullOrWhiteSpace(uId))
				return (null, $"Package '{packageName}' has no UId in the SysPackage response.");
			return (uId, null);
		}

		internal static (string uId, string error) QueryEntitySchemaUId(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string entitySchemaName) {
			var query = new JObject {
				[RootSchemaNameKey] = "SysSchema", [OperationTypeKey] = 0,
				[FiltersKey] = BuildFilterGroup(
					("byName", BuildEqFilter("Name", 1, entitySchemaName)),
					("byManager", BuildEqFilter("ManagerName", 1, "EntitySchemaManager"))),
				[ColumnsKey] = BuildUIdColumnSelection(),
				[RowCountKey] = 1
			};
			var rows = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (rows is null)
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

		internal static (JToken row, string error) QuerySysSchemaRow(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string schemaName,
			params (string alias, string path)[] columns) {
			var columnsItems = new JObject();
			foreach ((string alias, string path) in columns) {
				columnsItems[alias] = new JObject {
					["expression"] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = path }
				};
			}
			var query = new JObject {
				[RootSchemaNameKey] = "SysSchema", [OperationTypeKey] = 0,
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
							["leftExpression"] = new JObject {[ExpressionTypeKey] = 0, [ColumnPathKey] = "ManagerName"},
							["rightExpression"] = new JObject {[ExpressionTypeKey] = 2,
								["parameter"] = new JObject {["dataValueType"] = 1, ["value"] = "ClientUnitSchemaManager"}}
						}
					}
				},
				[ColumnsKey] = new JObject { [ItemsKey] = columnsItems },
				[RowCountKey] = 1
			};
			string dataServiceUrl = serviceUrlBuilder.Build(SelectQueryUrl);
			string metadataJson = applicationClient.ExecutePostRequest(dataServiceUrl, query.ToString(Formatting.None));
			var metadataResponse = JObject.Parse(metadataJson);
			if (!(metadataResponse["success"]?.Value<bool>() ?? false))
				return (null, "Failed to query schema metadata");
			var rows = metadataResponse["rows"] as JArray ?? new JArray();
			if (rows.Count == 0)
				return (null, $"Schema '{schemaName}' not found");
			return (rows[0], null);
		}
	}
}
