namespace Clio.Command {
	using System;
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
				(string packageUId, string packageError) = PageSchemaMetadataHelper.QueryPackageUId(_applicationClient, _serviceUrlBuilder, options.PackageName);
				if (packageError is not null) {
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
			if (!PageSchemaMetadataHelper.IsValidSchemaName(options.SchemaName)) {
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

		private bool SchemaNameExists(string schemaName) {
			(JToken row, _) = PageSchemaMetadataHelper.QuerySysSchemaRow(
				_applicationClient, _serviceUrlBuilder, schemaName, ("UId", "UId"));
			return row != null;
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
			error = PageSchemaMetadataHelper.ParseSaveErrorMessage(response, "Failed to create schema");
			return false;
		}

	}
}
