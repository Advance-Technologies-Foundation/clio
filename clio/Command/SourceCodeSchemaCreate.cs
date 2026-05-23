namespace Clio.Command {
	using System;
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

		private static readonly SchemaDesignerKind Kind = SchemaDesignerKind.SourceCode;

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
				if (options is null) {
					response = new SourceCodeSchemaCreateResponse { Success = false, Error = "options is required" };
					LogFailure(response.Error);
					return false;
				}
				string validationError = SchemaDesignerHelper.ValidateCreateInput(options.SchemaName, options.PackageName);
				if (validationError != null) {
					response = new SourceCodeSchemaCreateResponse { Success = false, Error = validationError };
					LogFailure(response.Error);
					return false;
				}
				LogStep(ref stepNumber, totalSteps, $"Resolving package '{options.PackageName}'");
				(string packageUId, string packageError) = PageSchemaMetadataHelper.QueryPackageUId(
					_applicationClient, _serviceUrlBuilder, options.PackageName);
				if (packageError != null) {
					response = new SourceCodeSchemaCreateResponse { Success = false, Error = packageError };
					LogFailure(response.Error);
					return false;
				}
				_logger.WriteInfo($"         package : {options.PackageName} (uId={packageUId})");
				LogStep(ref stepNumber, totalSteps, $"Checking schema-name uniqueness for '{options.SchemaName}'");
				if (SchemaDesignerHelper.SchemaNameExists(_applicationClient, _serviceUrlBuilder, options.SchemaName, Kind)) {
					response = new SourceCodeSchemaCreateResponse {
						Success = false,
						Error = $"Schema '{options.SchemaName}' already exists in this environment."
					};
					LogFailure(response.Error);
					return false;
				}
				string caption = string.IsNullOrWhiteSpace(options.Caption) ? options.SchemaName : options.Caption.Trim();
				LogStep(ref stepNumber, totalSteps, $"Creating schema '{options.SchemaName}' in package '{options.PackageName}'");
				(JObject schema, string createError) = SchemaDesignerHelper.CreateNewSchema(
					_applicationClient, _serviceUrlBuilder, packageUId, Kind);
				if (createError != null) {
					response = new SourceCodeSchemaCreateResponse { Success = false, Error = createError };
					LogFailure(response.Error);
					return false;
				}
				SchemaDesignerHelper.ApplySchemaMetadata(schema, options.SchemaName, caption, options.Description);
				string saveError = SchemaDesignerHelper.SaveSchema(_applicationClient, _serviceUrlBuilder, schema, Kind);
				if (saveError != null) {
					response = new SourceCodeSchemaCreateResponse { Success = false, Error = saveError };
					LogFailure(response.Error);
					return false;
				}
				string schemaUId = schema["uId"]?.ToString();
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
	}
}
