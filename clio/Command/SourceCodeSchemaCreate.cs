namespace Clio.Command {
	using System;
	using System.Linq;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	[Verb("create-schema", Aliases = ["schema-create"], HelpText = "Create a new C# source-code schema on a remote Creatio environment")]
	public class SourceCodeSchemaCreateOptions : EnvironmentOptions {
		[Option("schema-name", Required = true, HelpText = "New schema name, e.g. 'UsrMyHelper'")]
		public string SchemaName { get; set; }

		[Option("package-name", Required = true, HelpText = "Target package name that will own the new schema")]
		public string PackageName { get; set; }

		[Option("caption", Required = false, HelpText = "Optional display caption; defaults to schema-name")]
		public string Caption { get; set; }

		[Option("description", Required = false, HelpText = "Optional schema description")]
		public string Description { get; set; }
	}

	public class SourceCodeSchemaCreateResponse {
		public bool Success { get; set; }
		public string SchemaName { get; set; }
		public string SchemaUId { get; set; }
		public string PackageName { get; set; }
		public string PackageUId { get; set; }
		public string Caption { get; set; }
		public string Error { get; set; }
	}

	public class SourceCodeSchemaCreateCommand : Command<SourceCodeSchemaCreateOptions> {
		private const string SelectQueryRoute = "/DataService/json/SyncReply/SelectQuery";
		private const string CreateNewSchemaRoute = "ServiceModel/SourceCodeSchemaDesignerService.svc/CreateNewSchema";
		private const string SaveSchemaRoute = "ServiceModel/SourceCodeSchemaDesignerService.svc/SaveSchema";
		private const string SourceCodeSchemaManagerName = "SourceCodeSchemaManager";

		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ILogger _logger;

		public SourceCodeSchemaCreateCommand(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			ILogger logger) {
			_applicationClient = applicationClient;
			_serviceUrlBuilder = serviceUrlBuilder;
			_logger = logger;
		}

		public bool TryCreate(SourceCodeSchemaCreateOptions options, out SourceCodeSchemaCreateResponse response) {
			try {
				int stepNumber = 0;
				const int totalSteps = 4;

				LogStep(ref stepNumber, totalSteps, "Validating inputs");
				SourceCodeSchemaCreateResponse validationError = ValidateInput(options);
				if (validationError != null) {
					response = validationError;
					LogFailure(response.Error);
					return false;
				}

				LogStep(ref stepNumber, totalSteps, $"Resolving package '{options.PackageName}'");
				if (!TryResolvePackageUId(options.PackageName, out string packageUId, out string packageError)) {
					response = new SourceCodeSchemaCreateResponse { Success = false, Error = packageError };
					LogFailure(response.Error);
					return false;
				}
				_logger.WriteInfo($"         package : {options.PackageName} (uId={packageUId})");

				LogStep(ref stepNumber, totalSteps, $"Checking schema-name uniqueness for '{options.SchemaName}'");
				if (SchemaNameExists(options.SchemaName)) {
					response = new SourceCodeSchemaCreateResponse {
						Success = false,
						Error = $"Schema '{options.SchemaName}' already exists in this environment."
					};
					LogFailure(response.Error);
					return false;
				}

				string caption = string.IsNullOrWhiteSpace(options.Caption) ? options.SchemaName : options.Caption.Trim();

				LogStep(ref stepNumber, totalSteps, $"Creating schema '{options.SchemaName}' in package '{options.PackageName}'");
				if (!TryCreateAndSave(packageUId, options.SchemaName, caption, options.Description,
					out string schemaUId, out string createError)) {
					response = new SourceCodeSchemaCreateResponse { Success = false, Error = createError };
					LogFailure(response.Error);
					return false;
				}

				_logger.WriteInfo($"Schema '{options.SchemaName}' created successfully (schemaUId={schemaUId}).");
				response = new SourceCodeSchemaCreateResponse {
					Success = true,
					SchemaName = options.SchemaName,
					SchemaUId = schemaUId,
					PackageName = options.PackageName,
					PackageUId = packageUId,
					Caption = caption
				};
				return true;
			} catch (Exception ex) {
				response = new SourceCodeSchemaCreateResponse { Success = false, Error = ex.Message };
				LogFailure(response.Error);
				return false;
			}
		}

		public override int Execute(SourceCodeSchemaCreateOptions options) {
			bool success = TryCreate(options, out SourceCodeSchemaCreateResponse response);
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

		private static SourceCodeSchemaCreateResponse ValidateInput(SourceCodeSchemaCreateOptions options) {
			if (options is null) {
				return new SourceCodeSchemaCreateResponse { Success = false, Error = "options is required" };
			}
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				return new SourceCodeSchemaCreateResponse { Success = false, Error = "schema-name is required" };
			}
			if (!IsValidSchemaName(options.SchemaName)) {
				return new SourceCodeSchemaCreateResponse {
					Success = false,
					Error = "schema-name must start with a letter and contain only letters, digits, or underscores"
				};
			}
			if (string.IsNullOrWhiteSpace(options.PackageName)) {
				return new SourceCodeSchemaCreateResponse { Success = false, Error = "package-name is required" };
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
			var query = new JObject {
				["rootSchemaName"] = "SysSchema",
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
						["byName"] = new JObject {
							["filterType"] = 1,
							["comparisonType"] = 3,
							["isEnabled"] = true,
							["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = "Name" },
							["rightExpression"] = new JObject {
								["expressionType"] = 2,
								["parameter"] = new JObject { ["dataValueType"] = 1, ["value"] = schemaName }
							}
						},
						["byManager"] = new JObject {
							["filterType"] = 1,
							["comparisonType"] = 3,
							["isEnabled"] = true,
							["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = "ManagerName" },
							["rightExpression"] = new JObject {
								["expressionType"] = 2,
								["parameter"] = new JObject { ["dataValueType"] = 1, ["value"] = SourceCodeSchemaManagerName }
							}
						}
					}
				},
				["rowCount"] = 1
			};
			string url = _serviceUrlBuilder.Build(SelectQueryRoute);
			string responseJson = _applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
			JObject response = JObject.Parse(responseJson);
			var rows = response["rows"] as JArray ?? [];
			return rows.Count > 0;
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

		private bool TryCreateAndSave(string packageUId, string schemaName, string caption, string description,
			out string schemaUId, out string error) {
			schemaUId = null;
			error = null;

			string createUrl = _serviceUrlBuilder.Build(CreateNewSchemaRoute);
			var createRequest = new JObject { ["packageUId"] = packageUId };
			string createResponseJson = _applicationClient.ExecutePostRequest(
				createUrl, createRequest.ToString(Formatting.None));
			JObject createResponse = JObject.Parse(createResponseJson);
			if (!(createResponse["success"]?.Value<bool>() ?? false)) {
				error = createResponse["errorInfo"]?["message"]?.ToString() ?? "CreateNewSchema failed";
				return false;
			}
			if (createResponse["schema"] is not JObject schema) {
				error = "CreateNewSchema did not return a schema payload.";
				return false;
			}

			schema["name"] = schemaName;
			schema["caption"] = new JArray(new JObject { ["cultureName"] = "en-US", ["value"] = caption });
			if (!string.IsNullOrWhiteSpace(description)) {
				schema["description"] = new JArray(
					new JObject { ["cultureName"] = "en-US", ["value"] = description });
			}

			string saveUrl = _serviceUrlBuilder.Build(SaveSchemaRoute);
			string saveResponseJson = _applicationClient.ExecutePostRequest(
				saveUrl, schema.ToString(Formatting.None));
			JObject saveResponse = JObject.Parse(saveResponseJson);
			if (!(saveResponse["success"]?.Value<bool>() ?? false)) {
				error = BuildSaveErrorMessage(saveResponse);
				return false;
			}
			schemaUId = schema["uId"]?.ToString();
			return true;
		}

		private static string BuildSaveErrorMessage(JObject saveResponse) {
			string errorMessage = "Failed to save schema";
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
			return errorMessage;
		}
	}
}
