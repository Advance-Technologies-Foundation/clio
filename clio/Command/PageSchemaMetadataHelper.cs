namespace Clio.Command {
	using System;
	using System.IO;
	using System.Linq;
	using System.Net;
	using System.Net.Http;
	using System.Net.Sockets;
	using System.Threading.Tasks;
	using Clio.Command.McpServer;
	using Clio.Common;
	using Clio.Package;
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

		/// <summary>
		/// Operation label opening every transport/auth message produced by <see cref="ExecuteSelectQuery"/>,
		/// matching the label the other guarded <c>SelectQuery</c> call sites use.
		/// </summary>
		private const string SelectQueryOperationName = "SelectQuery";

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

		/// <summary>
		/// Executes a DataService <c>SelectQuery</c> and classifies the outcome into three distinct states, so
		/// that a failure to reach or authenticate against the environment is never reported as an answer about
		/// the requested data:
		/// <list type="bullet">
		/// <item><description><c>(rows, true, null)</c> — the service answered <c>success:true</c>.</description></item>
		/// <item><description><c>(empty, false, null)</c> — the service answered, and rejected the query
		/// (<c>success:false</c>). Only this state may be reported with a lookup-specific message.</description></item>
		/// <item><description><c>(empty, false, message)</c> — the request never produced a usable answer: the call
		/// timed out, the transport failed, the body was empty, or the body was an HTML login/error page instead of
		/// JSON. The message names the cause and the endpoint and must be surfaced verbatim.</description></item>
		/// </list>
		/// </summary>
		/// <param name="applicationClient">Authenticated Creatio HTTP client.</param>
		/// <param name="serviceUrlBuilder">Environment-aware URL builder.</param>
		/// <param name="query">DataService <c>SelectQuery</c> request body.</param>
		/// <returns>The selected rows, whether the service answered successfully, and the transport/auth error when there is one.</returns>
		/// <remarks>
		/// The non-JSON body messages come from <see cref="ServiceResponseJsonGuard"/> — the same authority the
		/// <c>SelectQuery</c> path fixed by ENG-93365 uses — so an expired session that answers with a login page
		/// produces one classified message across both copies of this plumbing rather than two divergent texts.
		/// Only the transport families that genuinely mean "this environment did not answer" are converted into a
		/// message (mirroring <c>CreatioVersionProvider.IsSoftDegradable</c>); every other exception propagates by
		/// design, because turning an unexpected programming error into a lookup failure is what hid these failures
		/// in the first place.
		/// </remarks>
		private static (JArray rows, bool success, string transportError) ExecuteSelectQuery(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			JObject query) {
			string url = serviceUrlBuilder.Build(SelectQueryUrl);
			string responseJson;
			try {
				responseJson = applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
			} catch (Exception ex) when (IsTimeout(ex)) {
				return (new JArray(), false, BuildTimeoutMessage(url, ex));
			} catch (Exception ex) when (IsTransportFailure(ex)) {
				return (new JArray(), false, BuildTransportMessage(url, ex));
			}

			if (string.IsNullOrWhiteSpace(responseJson))
				return (new JArray(), false,
					ServiceResponseJsonGuard.BuildEmptyBodyMessage(SelectQueryOperationName, url));

			JObject response;
			try {
				response = JObject.Parse(responseJson);
			} catch (JsonException parseException) {
				return (new JArray(), false, ServiceResponseJsonGuard.BuildNonJsonMessage(
					SelectQueryOperationName, url, responseJson, parseException));
			}

			return ReadSuccessFlag(response)
				? (response["rows"] as JArray ?? new JArray(), true, null)
				: (new JArray(), false, null);
		}

		/// <summary>
		/// Reads the DataService <c>success</c> flag leniently, exactly as the previous catch-all did: a missing
		/// flag or a value Newtonsoft cannot convert to a boolean counts as a rejected query, not as an unexpected
		/// failure. Keeping this conversion lenient is what lets the bare <c>catch</c> disappear without turning an
		/// oddly shaped-but-answered response into a propagating exception.
		/// </summary>
		/// <param name="response">Parsed DataService response body.</param>
		/// <returns><see langword="true"/> when the service reported success.</returns>
		private static bool ReadSuccessFlag(JObject response) {
			JToken successToken = response["success"];
			if (successToken is null)
				return false;
			try {
				return successToken.Value<bool>();
			} catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) {
				return false;
			}
		}

		/// <summary>
		/// Returns whether the exception means the request was sent but no response arrived in time. A read timeout
		/// reaches this code either as a <see cref="TaskCanceledException"/> (HttpClient-shaped clients) or as a
		/// <see cref="WebException"/> with <see cref="WebExceptionStatus.Timeout"/> (WebRequest-shaped clients), so
		/// both are classified as a timeout and kept distinct from a refused or failed connection.
		/// </summary>
		/// <param name="exception">Exception raised by the application client.</param>
		/// <returns><see langword="true"/> when the failure is a timeout.</returns>
		private static bool IsTimeout(Exception exception) =>
			exception is TaskCanceledException
				or TimeoutException
				|| (exception is WebException webException && webException.Status == WebExceptionStatus.Timeout);

		/// <summary>
		/// Returns whether the exception is one of the transport families that mean "this environment did not
		/// answer" — an HTTP error status, a refused or dropped connection, or a stream failure. Mirrors
		/// <c>CreatioVersionProvider.IsSoftDegradable</c> minus the JSON families, which are classified by the
		/// response-body branch instead.
		/// </summary>
		/// <param name="exception">Exception raised by the application client.</param>
		/// <returns><see langword="true"/> when the failure is a transport failure.</returns>
		private static bool IsTransportFailure(Exception exception) =>
			exception is HttpRequestException
				or WebException
				or SocketException
				or IOException;

		/// <summary>Builds the caller-actionable message for a <c>SelectQuery</c> that timed out.</summary>
		/// <param name="url">Endpoint the request was sent to.</param>
		/// <param name="exception">The timeout raised by the application client.</param>
		/// <returns>The message to surface.</returns>
		private static string BuildTimeoutMessage(string url, Exception exception) =>
			$"{SelectQueryOperationName} timed out before the environment answered (URL: {url}). "
			+ $"Detail: {SensitiveErrorTextRedactor.Redact(exception.GetReadableMessageException())}. "
			+ "This says nothing about the requested schema or package — retry the request, and if it persists "
			+ "check the environment health (healthcheck) and whether the target instance is still responding.";

		/// <summary>Builds the caller-actionable message for a <c>SelectQuery</c> that failed in transport.</summary>
		/// <param name="url">Endpoint the request was sent to.</param>
		/// <param name="exception">The transport failure raised by the application client.</param>
		/// <returns>The message to surface.</returns>
		private static string BuildTransportMessage(string url, Exception exception) =>
			$"{SelectQueryOperationName} could not be completed against the environment (URL: {url}). "
			+ $"Transport error: {SensitiveErrorTextRedactor.Redact(exception.GetReadableMessageException())}. "
			+ "This says nothing about the requested schema or package — verify that the environment is registered "
			+ "with valid credentials (reg-web-app, then healthcheck), that the site is running and reachable, and "
			+ "retry the request.";

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
			var (rows, success, transportError) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (transportError is not null)
				return (null, transportError);
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
			// Deliberately best-effort: this lookup only decorates a get-page response with a friendly package
			// name, and its string-returning signature has no channel for an error. A transport failure therefore
			// still degrades to "package name unknown" here rather than failing the whole call — the callers that
			// DO have an error channel are the ones that now report the classified transport message.
			var (rows, _, _) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
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
			var (rows, success, transportError) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (transportError is not null)
				return (null, transportError);
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
			var (rows, success, transportError) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (transportError is not null)
				return (null, transportError);
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
			var (rows, success, transportError) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (transportError is not null)
				return (null, transportError);
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
			// returns an HTML/redirect body then surfaces as an auth/transport error naming the login page and the
			// endpoint, rather than as a raw "Unexpected character '<'" parse failure or as a lookup failure that
			// would send the caller looking at their schema.
			var (rows, success, transportError) = ExecuteSelectQuery(applicationClient, serviceUrlBuilder, query);
			if (transportError is not null)
				return (null, transportError);
			if (!success)
				return (null, "Failed to query schema metadata");
			if (rows.Count == 0)
				return (null, $"Schema '{schemaName}' not found");
			return (rows[0], null);
		}
	}
}
