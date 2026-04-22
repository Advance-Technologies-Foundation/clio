namespace Clio.Command {
	using System;
	using System.Linq;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	[Verb("create-client-unit-schema", Aliases = ["client-unit-schema-create"],
		HelpText = "Create a new JavaScript (ClientUnit) schema on a remote Creatio environment")]
	public class ClientUnitSchemaCreateOptions : EnvironmentOptions {
		[Option("schema-name", Required = true, HelpText = "New schema name, e.g. 'UsrMyHelper'")]
		public string SchemaName { get; set; }

		[Option("package-name", Required = true, HelpText = "Target package name that will own the new schema")]
		public string PackageName { get; set; }

		[Option("caption", Required = false, HelpText = "Optional display caption; defaults to schema-name")]
		public string Caption { get; set; }

		[Option("description", Required = false, HelpText = "Optional schema description")]
		public string Description { get; set; }
	}

	public class ClientUnitSchemaCreateResponse {
		public bool Success { get; set; }
		public string SchemaName { get; set; }
		public string SchemaUId { get; set; }
		public string PackageName { get; set; }
		public string PackageUId { get; set; }
		public string Caption { get; set; }
		public string Error { get; set; }
	}

	public class ClientUnitSchemaCreateCommand : Command<ClientUnitSchemaCreateOptions> {
		private const string SelectQueryRoute = "/DataService/json/SyncReply/SelectQuery";
		private const string SaveSchemaRoute = "/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
		private const string ClientUnitManagerName = "ClientUnitSchemaManager";

		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;

		public ClientUnitSchemaCreateCommand(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			ILogger logger) {
			_applicationClient = applicationClient;
			_serviceUrlBuilder = serviceUrlBuilder;
			_logger = logger;
		}

		public bool TryCreate(ClientUnitSchemaCreateOptions options, out ClientUnitSchemaCreateResponse response) {
			try {
				int stepNumber = 0;
				const int totalSteps = 4;

				LogStep(ref stepNumber, totalSteps, "Validating inputs");
				ClientUnitSchemaCreateResponse validationError = ValidateInput(options);
				if (validationError != null) {
					response = validationError;
					LogFailure(response.Error);
					return false;
				}

				LogStep(ref stepNumber, totalSteps, $"Resolving package '{options.PackageName}'");
				if (!TryResolvePackageUId(options.PackageName, out string packageUId, out string packageError)) {
					response = new ClientUnitSchemaCreateResponse { Success = false, Error = packageError };
					LogFailure(response.Error);
					return false;
				}
				_logger.WriteInfo($"         package : {options.PackageName} (uId={packageUId})");

				LogStep(ref stepNumber, totalSteps, $"Checking schema-name uniqueness for '{options.SchemaName}'");
				if (SchemaNameExists(options.SchemaName)) {
					response = new ClientUnitSchemaCreateResponse {
						Success = false,
						Error = $"Schema '{options.SchemaName}' already exists in this environment."
					};
					LogFailure(response.Error);
					return false;
				}

				string caption = string.IsNullOrWhiteSpace(options.Caption) ? options.SchemaName : options.Caption.Trim();
				string newSchemaUId = Guid.NewGuid().ToString("D");

				LogStep(ref stepNumber, totalSteps, $"Saving schema '{options.SchemaName}' (uId={newSchemaUId})");
				JObject payload = BuildSaveSchemaPayload(newSchemaUId, options.SchemaName, caption,
					options.Description, packageUId, options.PackageName);
				if (!TrySaveSchema(payload, out string saveError)) {
					response = new ClientUnitSchemaCreateResponse { Success = false, Error = saveError };
					LogFailure(response.Error);
					return false;
				}

				_logger.WriteInfo($"Schema '{options.SchemaName}' created successfully (schemaUId={newSchemaUId}).");
				response = new ClientUnitSchemaCreateResponse {
					Success = true,
					SchemaName = options.SchemaName,
					SchemaUId = newSchemaUId,
					PackageName = options.PackageName,
					PackageUId = packageUId,
					Caption = caption
				};
				return true;
			} catch (Exception ex) {
				response = new ClientUnitSchemaCreateResponse { Success = false, Error = ex.Message };
				LogFailure(response.Error);
				return false;
			}
		}

		public override int Execute(ClientUnitSchemaCreateOptions options) {
			bool success = TryCreate(options, out ClientUnitSchemaCreateResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}

		private void LogStep(ref int stepNumber, int totalSteps, string message) {
			stepNumber++;
			_logger.WriteInfo($"[{stepNumber}/{totalSteps}] {message}...");
		}

		private void LogFailure(string error) {
			_logger.WriteInfo($"  failed: {error}");
		}

		private static ClientUnitSchemaCreateResponse ValidateInput(ClientUnitSchemaCreateOptions options) {
			if (options is null) {
				return new ClientUnitSchemaCreateResponse { Success = false, Error = "options is required" };
			}
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				return new ClientUnitSchemaCreateResponse { Success = false, Error = "schema-name is required" };
			}
			if (!IsValidSchemaName(options.SchemaName)) {
				return new ClientUnitSchemaCreateResponse {
					Success = false,
					Error = "schema-name must start with a letter and contain only letters, digits, or underscores"
				};
			}
			if (string.IsNullOrWhiteSpace(options.PackageName)) {
				return new ClientUnitSchemaCreateResponse { Success = false, Error = "package-name is required" };
			}
			return null;
		}

		private static bool IsValidSchemaName(string name) {
			if (string.IsNullOrEmpty(name) || !char.IsLetter(name[0])) {
				return false;
			}
			return name.All(c => char.IsLetterOrDigit(c) || c == '_');
		}

		private bool SchemaNameExists(string schemaName) {
			(JToken row, _) = PageSchemaMetadataHelper.QuerySysSchemaRow(
				_applicationClient, _serviceUrlBuilder, schemaName, ("UId", "UId"));
			return row != null;
		}

		private bool TryResolvePackageUId(string packageName, out string packageUId, out string error) {
			packageUId = null;
			error = null;
			var query = new JObject {
				["rootSchemaName"] = "SysPackage",
				["operationType"] = 0,
				["columns"] = new JObject {
					["items"] = new JObject {
						["UId"] = new JObject {
							["expression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = "UId" }
						}
					}
				},
				["filters"] = new JObject {
					["filterType"] = 6,
					["logicalOperation"] = 0,
					["isEnabled"] = true,
					["items"] = new JObject {
						["filter0"] = new JObject {
							["filterType"] = 1,
							["comparisonType"] = 3,
							["isEnabled"] = true,
							["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = "Name" },
							["rightExpression"] = new JObject {
								["expressionType"] = 2,
								["parameter"] = new JObject { ["dataValueType"] = 1, ["value"] = packageName }
							}
						}
					}
				},
				["rowCount"] = 1
			};
			string url = _serviceUrlBuilder.Build(SelectQueryRoute);
			string responseJson = _applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
			JObject response = JObject.Parse(responseJson);
			if (!(response["success"]?.Value<bool>() ?? false)) {
				error = "Failed to query SysPackage";
				return false;
			}
			var rows = response["rows"] as JArray ?? [];
			if (rows.Count == 0) {
				error = $"Package '{packageName}' not found in the target environment.";
				return false;
			}
			packageUId = rows[0]["UId"]?.ToString();
			if (string.IsNullOrWhiteSpace(packageUId)) {
				error = $"Package '{packageName}' has no UId in the SysPackage response.";
				return false;
			}
			return true;
		}

		private static JObject BuildSaveSchemaPayload(string newSchemaUId, string schemaName, string caption,
			string description, string packageUId, string packageName) {
			var localizableCaption = new JObject { ["cultureName"] = "en-US", ["value"] = caption };
			return new JObject {
				["uId"] = newSchemaUId,
				["name"] = schemaName,
				["caption"] = new JArray { localizableCaption },
				["description"] = string.IsNullOrWhiteSpace(description) ? new JArray() : new JArray(new JObject {
					["cultureName"] = "en-US",
					["value"] = description
				}),
				["package"] = new JObject {
					["uId"] = packageUId,
					["name"] = packageName
				},
				["managerName"] = ClientUnitManagerName,
				["extendParent"] = false,
				["body"] = string.Empty,
				["localizableStrings"] = new JArray(),
				["parameters"] = new JArray(),
				["messages"] = new JArray(),
				["images"] = new JArray()
			};
		}

		private bool TrySaveSchema(JObject payload, out string error) {
			error = null;
			string url = _serviceUrlBuilder.Build(SaveSchemaRoute);
			string responseJson = _applicationClient.ExecutePostRequest(url, payload.ToString(Formatting.None));
			JObject response = JObject.Parse(responseJson);
			if (response["success"]?.Value<bool>() ?? false) {
				return true;
			}
			error = BuildSaveErrorMessage(response);
			return false;
		}

		private static string BuildSaveErrorMessage(JObject saveResponse) {
			string errorMessage = "Failed to create schema";
			if (saveResponse["errorInfo"] is JObject errorInfo) {
				string infoMessage = errorInfo["message"]?.ToString();
				if (!string.IsNullOrWhiteSpace(infoMessage)) {
					errorMessage = infoMessage;
				}
			}
			if (saveResponse["validationErrors"] is JArray validationErrors && validationErrors.Count > 0) {
				var messages = validationErrors
					.Select(e => e["message"]?.ToString() ?? e["caption"]?.ToString())
					.Where(m => !string.IsNullOrWhiteSpace(m));
				errorMessage = string.Join("; ", messages);
			}
			if (saveResponse["addonsErrors"] is JArray addonsErrors && addonsErrors.Count > 0) {
				errorMessage = string.Join("; ", addonsErrors.Select(e => e.ToString()));
			}
			return errorMessage;
		}
	}
}
