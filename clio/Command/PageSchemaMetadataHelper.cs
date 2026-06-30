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
		/// Resolves a Freedom UI page (client-unit) schema name to its <c>UId</c> via the DataService
		/// SelectQuery endpoint, filtered to <c>ClientUnitSchemaManager</c>. Used by
		/// <c>create-related-page-addon</c> to turn page names into the <c>PageSchemaUId</c> values stored
		/// in the RelatedPage add-on metadata.
		/// </summary>
		/// <remarks>
		/// Name-to-UId schema resolution intentionally uses the DataService <c>SelectQuery</c> over
		/// <c>SysSchema</c> rather than a ClioGate endpoint. This is the established, repo-consistent pattern
		/// for these read-only lookups — the same helper backs <c>create-page-business-rules</c>
		/// (<c>PageBusinessRuleSchemaProvider</c>) and <c>create-page</c>, and none of them introduce a
		/// ClioGate dependency. ClioGate is reserved for privileged write/elevated operations. The trade-off:
		/// the caller must have DataService read access to <c>SysSchema</c> (a full schema-management user);
		/// a restricted solution-management user without that access would get a SecurityException here. This
		/// is a pre-existing, repo-wide limitation, accepted for consistency, not introduced by this command.
		/// </remarks>
		internal static (string uId, string error) QueryPageSchemaUId(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string pageSchemaName) {
			(JToken row, string error) = QuerySysSchemaRow(applicationClient, serviceUrlBuilder, pageSchemaName, ("UId", "UId"));
			if (row == null) {
				return (null, error ?? $"Page schema '{pageSchemaName}' not found.");
			}
			string uId = row["UId"]?.ToString();
			if (string.IsNullOrWhiteSpace(uId)) {
				return (null, $"Page schema '{pageSchemaName}' has no UId in the SysSchema response.");
			}
			return (uId, null);
		}

		/// <summary>
		/// Resolves a Creatio role (<c>SysAdminUnit</c>) name to its <c>Id</c> via the DataService
		/// SelectQuery endpoint. Used by <c>create-related-page-addon</c> to scope a related-page set to an
		/// audience by name (e.g. <c>All employees</c> or the portal role <c>All external users</c>) instead
		/// of requiring the caller to pass a role GUID.
		/// </summary>
		internal static (string uId, string error) QueryRoleUId(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			string roleName) {
			var query = new JObject {
				[RootSchemaNameKey] = "SysAdminUnit", [OperationTypeKey] = 0,
				[FiltersKey] = BuildFilterGroup(("byName", BuildEqFilter("Name", 1, roleName))),
				[ColumnsKey] = new JObject {
					[ItemsKey] = new JObject {
						["Id"] = new JObject { [ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "Id" } }
					}
				},
				[RowCountKey] = 1
			};
			var (rows, success) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (!success)
				return (null, "Failed to query SysAdminUnit");
			if (rows.Count == 0)
				return (null, $"Role '{roleName}' not found.");
			string id = rows[0]["Id"]?.ToString();
			if (string.IsNullOrWhiteSpace(id))
				return (null, $"Role '{roleName}' has no Id in the SysAdminUnit response.");
			return (id, null);
		}

		/// <summary>
		/// Reverse of <see cref="QueryPageSchemaUId"/>: resolves a page (client-unit) schema <c>UId</c> back to
		/// its <c>Name</c> via the DataService SelectQuery endpoint. Used by <c>get-related-page-addon</c> to
		/// surface friendly page names for the UIds stored in the RelatedPage add-on metadata. Returns
		/// <c>null</c> when the UId is empty or the schema is not found.
		/// </summary>
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
