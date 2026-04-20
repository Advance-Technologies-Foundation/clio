namespace Clio.Command {
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using Clio.Common;
	using CommandLine;
	using Newtonsoft.Json;
	using Newtonsoft.Json.Linq;

	/// <summary>
	/// Options for the <c>create-page</c> command.
	/// </summary>
	[Verb("create-page", Aliases = ["page-create"], HelpText = "Create a new Freedom UI page from a supported template")]
	public class PageCreateOptions : EnvironmentOptions {
		[Option("schema-name", Required = true, HelpText = "New page schema name, e.g. 'UsrMyApp_BlankPage'")]
		public string SchemaName { get; set; }

		[Option("template", Required = true, HelpText = "Template name or UId from list-page-templates (e.g. 'BlankPageTemplate')")]
		public string Template { get; set; }

		[Option("package-name", Required = true, HelpText = "Target package name that will own the new page schema")]
		public string PackageName { get; set; }

		[Option("caption", Required = false, HelpText = "Optional display caption; defaults to schema-name")]
		public string Caption { get; set; }

		[Option("description", Required = false, HelpText = "Optional schema description")]
		public string Description { get; set; }

		[Option("entity-schema-name", Required = false, HelpText = "Optional entity schema name to record in the new page dependencies")]
		public string EntitySchemaName { get; set; }
	}

	/// <summary>
	/// Creates a new Freedom UI page schema from a supported template.
	/// </summary>
	public class PageCreateCommand : Command<PageCreateOptions> {
		private const string SchemaSaveRoute = "/ServiceModel/ClientUnitSchemaDesignerService.svc/SaveSchema";
		private const string SelectQueryRoute = "/DataService/json/SyncReply/SelectQuery";
		private const string ClientUnitManagerName = "ClientUnitSchemaManager";

		private readonly IApplicationClient _applicationClient;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly ISchemaTemplateCatalog _templateCatalog;
		private readonly ILogger _logger;

		public PageCreateCommand(
			IApplicationClient applicationClient,
			IServiceUrlBuilder serviceUrlBuilder,
			ISchemaTemplateCatalog templateCatalog,
			ILogger logger) {
			_applicationClient = applicationClient;
			_serviceUrlBuilder = serviceUrlBuilder;
			_templateCatalog = templateCatalog;
			_logger = logger;
		}

		public bool TryCreatePage(PageCreateOptions options, out PageCreateResponse response) {
			try {
				bool hasEntity = options is not null && !string.IsNullOrWhiteSpace(options.EntitySchemaName);
				int totalSteps = 5 + (hasEntity ? 1 : 0);
				int stepNumber = 0;
				LogStep(ref stepNumber, totalSteps, "Validating inputs");
				PageCreateResponse validationError = ValidateInput(options);
				if (validationError != null) {
					response = validationError;
					LogFailure(response.Error);
					return false;
				}
				LogStep(ref stepNumber, totalSteps, $"Resolving template '{options.Template}'");
				PageTemplateInfo template;
				try {
					template = _templateCatalog.FindTemplate(options.Template);
				} catch (Exception ex) {
					response = new PageCreateResponse { Success = false, Error = $"Failed to resolve template catalog: {ex.Message}" };
					LogFailure(response.Error);
					return false;
				}
				if (template is null) {
					response = new PageCreateResponse {
						Success = false,
						Error = $"Template '{options.Template}' is not supported. Use list-page-templates to discover valid values."
					};
					LogFailure(response.Error);
					return false;
				}
				_logger.WriteInfo($"         template: {template.Name} (uId={template.UId}, group={template.GroupName}, type={DescribeSchemaType(template.SchemaType)})");
				LogStep(ref stepNumber, totalSteps, $"Resolving package '{options.PackageName}'");
				if (!TryResolvePackageUId(options.PackageName, out string packageUId, out string packageError)) {
					response = new PageCreateResponse { Success = false, Error = packageError };
					LogFailure(response.Error);
					return false;
				}
				_logger.WriteInfo($"         package : {options.PackageName} (uId={packageUId})");
				LogStep(ref stepNumber, totalSteps, $"Checking schema-name uniqueness for '{options.SchemaName}'");
				if (SchemaNameExists(options.SchemaName)) {
					response = new PageCreateResponse {
						Success = false,
						Error = $"Page schema '{options.SchemaName}' already exists in this environment."
					};
					LogFailure(response.Error);
					return false;
				}
				string caption = string.IsNullOrWhiteSpace(options.Caption) ? options.SchemaName : options.Caption.Trim();
				string entitySchemaUId = null;
				if (hasEntity) {
					LogStep(ref stepNumber, totalSteps, $"Resolving entity schema '{options.EntitySchemaName}'");
					if (!TryResolveEntitySchemaUId(options.EntitySchemaName, out entitySchemaUId, out string entityError)) {
						response = new PageCreateResponse { Success = false, Error = entityError };
						LogFailure(response.Error);
						return false;
					}
					_logger.WriteInfo($"         entity  : {options.EntitySchemaName} (uId={entitySchemaUId})");
				}
				string newSchemaUId = Guid.NewGuid().ToString("D");
				LogStep(ref stepNumber, totalSteps, $"Saving schema via ClientUnitSchemaDesignerService (uId={newSchemaUId})");
				JObject payload = BuildSaveSchemaPayload(
					newSchemaUId, options.SchemaName, caption, options.Description,
					template, packageUId, options.PackageName, entitySchemaUId);
				if (!TrySaveSchema(payload, out string saveError)) {
					response = new PageCreateResponse { Success = false, Error = saveError };
					LogFailure(response.Error);
					return false;
				}
				_logger.WriteInfo($"Page '{options.SchemaName}' created successfully (schemaUId={newSchemaUId}).");
				response = new PageCreateResponse {
					Success = true,
					SchemaName = options.SchemaName,
					SchemaUId = newSchemaUId,
					PackageName = options.PackageName,
					PackageUId = packageUId,
					TemplateName = template.Name,
					TemplateUId = template.UId,
					Caption = caption,
					EntitySchemaName = options.EntitySchemaName,
					EntitySchemaUId = entitySchemaUId
				};
				return true;
			} catch (Exception ex) {
				response = new PageCreateResponse { Success = false, Error = ex.Message };
				LogFailure(response.Error);
				return false;
			}
		}

		private void LogStep(ref int stepNumber, int totalSteps, string message) {
			stepNumber++;
			_logger.WriteInfo($"[{stepNumber}/{totalSteps}] {message}...");
		}

		private void LogFailure(string error) {
			_logger.WriteInfo($"  failed: {error}");
		}

		private static string DescribeSchemaType(int schemaType) => schemaType switch {
			9 => "web",
			10 => "mobile",
			_ => schemaType.ToString()
		};

		public override int Execute(PageCreateOptions options) {
			bool success = TryCreatePage(options, out PageCreateResponse response);
			_logger.WriteInfo(JsonConvert.SerializeObject(response));
			return success ? 0 : 1;
		}

		private static PageCreateResponse ValidateInput(PageCreateOptions options) {
			if (options is null) {
				return new PageCreateResponse { Success = false, Error = "options is required" };
			}
			if (string.IsNullOrWhiteSpace(options.SchemaName)) {
				return new PageCreateResponse { Success = false, Error = "schema-name is required" };
			}
			if (!IsValidSchemaName(options.SchemaName)) {
				return new PageCreateResponse {
					Success = false,
					Error = "schema-name must start with a letter and contain only letters, digits, or underscores"
				};
			}
			if (string.IsNullOrWhiteSpace(options.Template)) {
				return new PageCreateResponse { Success = false, Error = "template is required" };
			}
			if (string.IsNullOrWhiteSpace(options.PackageName)) {
				return new PageCreateResponse { Success = false, Error = "package-name is required" };
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
			var response = JObject.Parse(responseJson);
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

		private bool TryResolveEntitySchemaUId(string entitySchemaName, out string entitySchemaUId, out string error) {
			entitySchemaUId = null;
			error = null;
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
						["filter0"] = new JObject {
							["filterType"] = 1,
							["comparisonType"] = 3,
							["isEnabled"] = true,
							["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = "Name" },
							["rightExpression"] = new JObject {
								["expressionType"] = 2,
								["parameter"] = new JObject { ["dataValueType"] = 1, ["value"] = entitySchemaName }
							}
						},
						["filter1"] = new JObject {
							["filterType"] = 1,
							["comparisonType"] = 3,
							["isEnabled"] = true,
							["leftExpression"] = new JObject { ["expressionType"] = 0, ["columnPath"] = "ManagerName" },
							["rightExpression"] = new JObject {
								["expressionType"] = 2,
								["parameter"] = new JObject { ["dataValueType"] = 1, ["value"] = "EntitySchemaManager" }
							}
						}
					}
				},
				["rowCount"] = 1
			};
			string url = _serviceUrlBuilder.Build(SelectQueryRoute);
			string responseJson = _applicationClient.ExecutePostRequest(url, query.ToString(Formatting.None));
			var response = JObject.Parse(responseJson);
			if (!(response["success"]?.Value<bool>() ?? false)) {
				error = "Failed to query entity schema metadata";
				return false;
			}
			var rows = response["rows"] as JArray ?? [];
			if (rows.Count == 0) {
				error = $"Entity schema '{entitySchemaName}' not found.";
				return false;
			}
			entitySchemaUId = rows[0]["UId"]?.ToString();
			return true;
		}

		private static JObject BuildSaveSchemaPayload(
			string newSchemaUId, string schemaName, string caption, string description,
			PageTemplateInfo template, string packageUId, string packageName, string entitySchemaUId) {
			var localizableCaption = new JObject { ["cultureName"] = "en-US", ["value"] = caption };
			var schema = new JObject {
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
				["parent"] = new JObject {
					["uId"] = template.UId,
					["name"] = template.Name
				},
				["extendParent"] = false,
				["body"] = string.Empty,
				["localizableStrings"] = new JArray(),
				["parameters"] = new JArray(),
				["messages"] = new JArray(),
				["images"] = new JArray()
			};
			if (!string.IsNullOrWhiteSpace(entitySchemaUId)) {
				schema["dependsOn"] = new JArray(new JObject {
					["uId"] = entitySchemaUId
				});
			}
			return schema;
		}

		private bool TrySaveSchema(JObject schemaToSave, out string error) {
			error = null;
			string url = _serviceUrlBuilder.Build(SchemaSaveRoute);
			string responseJson = _applicationClient.ExecutePostRequest(url, schemaToSave.ToString(Formatting.None));
			var response = JObject.Parse(responseJson);
			if (response["success"]?.Value<bool>() ?? false) {
				return true;
			}
			error = BuildSaveErrorMessage(response);
			return false;
		}

		private static string BuildSaveErrorMessage(JObject saveResponse) {
			string errorMessage = "Failed to create page schema";
			if (saveResponse["errorInfo"] is JObject errorInfo) {
				string infoMessage = errorInfo["message"]?.ToString();
				if (!string.IsNullOrWhiteSpace(infoMessage)) {
					errorMessage = infoMessage;
				}
			}
			if (saveResponse["validationErrors"] is JArray validationErrors && validationErrors.Count > 0) {
				IEnumerable<string> messages = validationErrors
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
