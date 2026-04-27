using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Workspaces;
using CommandLine;
using Newtonsoft.Json.Linq;

namespace Clio.Command;

[Verb("delete-schema", HelpText = "Delete a schema from a workspace package or from a remote Creatio environment")]
public class DeleteSchemaOptions : RemoteCommandOptions {

	[Value(0, MetaName = "SchemaName", Required = true, HelpText = "Schema name")]
	public string SchemaName { get; set; }

	[Option("workspace-path", Required = false, Hidden = true,
		HelpText = "Workspace path override. Intended for MCP usage.")]
	public string WorkspacePath { get; set; }

	[Option("remote", Required = false,
		HelpText = "Delete the schema directly from the remote Creatio environment (no workspace required)")]
	public bool Remote { get; set; }
}

public sealed class DeleteSchemaRemoteResponse {

	[System.Text.Json.Serialization.JsonPropertyName("success")]
	public bool Success { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("schemaName")]
	public string SchemaName { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("schemaUId")]
	public string SchemaUId { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("managerName")]
	public string ManagerName { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("packageName")]
	public string PackageName { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

public class DeleteSchemaCommand : RemoteCommand<DeleteSchemaOptions> {
	private const string ExpressionKey = "expression";
	private const string ExpressionTypeKey = "expressionType";
	private const string ColumnPathKey = "columnPath";

	private static readonly JsonSerializerOptions SerializerOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IJsonConverter _jsonConverter;
	private readonly IFileSystem _fileSystem;

	public DeleteSchemaCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder, IWorkspacePathBuilder workspacePathBuilder,
		IJsonConverter jsonConverter, IFileSystem fileSystem)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
		_workspacePathBuilder = workspacePathBuilder;
		_jsonConverter = jsonConverter;
		_fileSystem = fileSystem;
	}

	protected override void ExecuteRemoteCommand(DeleteSchemaOptions options) {
		string schemaName = options.SchemaName?.Trim();
		if (string.IsNullOrWhiteSpace(schemaName)) {
			throw new InvalidOperationException("Schema name cannot be empty.");
		}
		if (options.Remote) {
			if (!TryDeleteRemote(schemaName, out DeleteSchemaRemoteResponse remoteResponse)) {
				throw new InvalidOperationException(remoteResponse.Error);
			}
			Logger.WriteInfo(
				$"Deleted schema '{remoteResponse.SchemaName}' (uId={remoteResponse.SchemaUId}) from package '{remoteResponse.PackageName}'.");
			return;
		}
		ConfigureWorkspace(options);
		EnsureWorkspace();

		HashSet<string> workspacePackages = GetWorkspacePackages();
		WorkspaceExplorerItemDto schemaItem = FindWorkspaceSchemaItem(schemaName, workspacePackages);

		Logger.WriteInfo($"Deleting schema '{schemaItem.Name}' from package '{schemaItem.PackageName}'...");
		string deleteUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeleteWorkspaceItem);
		string deleteRequestBody = JsonSerializer.Serialize(new[] { schemaItem });
		string deleteResponseJson = ApplicationClient.ExecutePostRequest(deleteUrl, deleteRequestBody, RequestTimeout,
			RetryCount, DelaySec);

		DeleteWorkspaceItemsResponse deleteResponse =
			Deserialize<DeleteWorkspaceItemsResponse>(deleteResponseJson, "Delete");
		EnsureDeleteSucceeded(deleteResponse, schemaItem.Name, schemaItem.PackageName);
		Logger.WriteInfo($"Deleted schema '{schemaItem.Name}' from package '{schemaItem.PackageName}'.");
	}

	internal bool TryDeleteRemote(string schemaName, out DeleteSchemaRemoteResponse response) {
		try {
			if (string.IsNullOrWhiteSpace(schemaName)) {
				response = new DeleteSchemaRemoteResponse {
					Success = false,
					Error = "schema-name is required"
				};
				return false;
			}
			if (!TryResolveSysSchema(schemaName, out SysSchemaRemoteRecord record, out string resolveError)) {
				response = new DeleteSchemaRemoteResponse { Success = false, Error = resolveError };
				return false;
			}
			WorkspaceExplorerItemDto dto = new() {
				Id = record.Id,
				UId = record.UId,
				Name = schemaName,
				PackageUId = record.PackageUId,
				PackageName = record.PackageName
			};
			string deleteUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeleteWorkspaceItem);
			string requestBody = JsonSerializer.Serialize(new[] { dto });
			string responseJson = ApplicationClient.ExecutePostRequest(
				deleteUrl, requestBody, RequestTimeout, RetryCount, DelaySec);
			DeleteWorkspaceItemsResponse deleteResponse =
				Deserialize<DeleteWorkspaceItemsResponse>(responseJson, "Delete");
			if (!deleteResponse.Success || deleteResponse.RowsAffected <= 0) {
				string message = string.IsNullOrWhiteSpace(deleteResponse.ErrorInfo?.Message)
					? $"Failed to delete schema '{schemaName}' from remote environment."
					: deleteResponse.ErrorInfo.Message;
				response = new DeleteSchemaRemoteResponse {
					Success = false,
					SchemaName = schemaName,
					SchemaUId = record.UId.ToString(),
					ManagerName = record.ManagerName,
					PackageName = record.PackageName,
					Error = message
				};
				return false;
			}
			response = new DeleteSchemaRemoteResponse {
				Success = true,
				SchemaName = schemaName,
				SchemaUId = record.UId.ToString(),
				ManagerName = record.ManagerName,
				PackageName = record.PackageName
			};
			return true;
		}
		catch (Exception ex) {
			response = new DeleteSchemaRemoteResponse { Success = false, Error = ex.Message };
			return false;
		}
	}

	private bool TryResolveSysSchema(string schemaName, out SysSchemaRemoteRecord record, out string error) {
		record = null;
		error = null;
		var query = new JObject {
			["rootSchemaName"] = "SysSchema",
			["operationType"] = 0,
			["columns"] = new JObject {
				["items"] = new JObject {
					["Id"] = new JObject {
						[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "Id" }
					},
					["UId"] = new JObject {
						[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "UId" }
					},
					["ManagerName"] = new JObject {
						[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "ManagerName" }
					},
					["PackageUId"] = new JObject {
						[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "SysPackage.UId" }
					},
					["PackageName"] = new JObject {
						[ExpressionKey] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "SysPackage.Name" }
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
						["leftExpression"] = new JObject { [ExpressionTypeKey] = 0, [ColumnPathKey] = "Name" },
						["rightExpression"] = new JObject {
							[ExpressionTypeKey] = 2,
							["parameter"] = new JObject { ["dataValueType"] = 1, ["value"] = schemaName }
						}
					}
				}
			},
			["rowCount"] = 1
		};
		string selectUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select);
		string responseJson = ApplicationClient.ExecutePostRequest(
			selectUrl, query.ToString(Newtonsoft.Json.Formatting.None), RequestTimeout, RetryCount, DelaySec);
		JObject selectResponse = JObject.Parse(responseJson);
		var rows = selectResponse["rows"] as JArray ?? [];
		if (rows.Count == 0) {
			error = $"Schema '{schemaName}' not found in the target environment.";
			return false;
		}
		JToken row = rows[0];
		if (!Guid.TryParse(row["UId"]?.ToString(), out Guid uId)) {
			error = $"Schema '{schemaName}' metadata is missing UId.";
			return false;
		}
		Guid.TryParse(row["Id"]?.ToString(), out Guid id);
		Guid.TryParse(row["PackageUId"]?.ToString(), out Guid packageUId);
		record = new SysSchemaRemoteRecord {
			Id = id,
			UId = uId,
			ManagerName = row["ManagerName"]?.ToString(),
			PackageUId = packageUId,
			PackageName = row["PackageName"]?.ToString()
		};
		return true;
	}

	private void ConfigureWorkspace(DeleteSchemaOptions options) {
		if (!string.IsNullOrWhiteSpace(options.WorkspacePath)) {
			_workspacePathBuilder.RootPath = options.WorkspacePath.Trim();
		}
	}

	private void EnsureWorkspace() {
		if (!_fileSystem.ExistsDirectory(_workspacePathBuilder.RootPath) || !_workspacePathBuilder.IsWorkspace) {
			throw new InvalidOperationException(
				"Current directory is not a workspace. Please run this command from a workspace directory.");
		}
	}

	private HashSet<string> GetWorkspacePackages() {
		WorkspaceSettings workspaceSettings =
			_jsonConverter.DeserializeObjectFromFile<WorkspaceSettings>(_workspacePathBuilder.WorkspaceSettingsPath);
		IEnumerable<string> packages = workspaceSettings?.Packages ?? [];
		HashSet<string> workspacePackages = new(packages, StringComparer.OrdinalIgnoreCase);
		if (workspacePackages.Count == 0) {
			throw new InvalidOperationException("The current workspace does not contain any packages.");
		}
		return workspacePackages;
	}

	private WorkspaceExplorerItemDto FindWorkspaceSchemaItem(string schemaName, HashSet<string> workspacePackages) {
		string getWorkspaceItemsUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems);
		string responseJson = ApplicationClient.ExecutePostRequest(getWorkspaceItemsUrl, string.Empty, RequestTimeout,
			RetryCount, DelaySec);

		WorkspaceItemsResponse response = Deserialize<WorkspaceItemsResponse>(responseJson, "GetWorkspaceItems");
		List<WorkspaceExplorerItemDto> matches = (response.Items ?? [])
			.Where(item => workspacePackages.Contains(item.PackageName)
				&& string.Equals(item.Name, schemaName, StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (matches.Count == 0) {
			throw new InvalidOperationException($"Schema '{schemaName}' is not part of the current workspace.");
		}

		if (matches.Count > 1) {
			string packages = string.Join(", ", matches.Select(item => item.PackageName).Distinct(StringComparer.OrdinalIgnoreCase));
			throw new InvalidOperationException(
				$"Schema '{schemaName}' exists in multiple workspace packages: {packages}. Delete is ambiguous.");
		}

		return matches[0];
	}

	private static T Deserialize<T>(string json, string operation) {
		T result = JsonSerializer.Deserialize<T>(json, SerializerOptions);
		return result ?? throw new InvalidOperationException($"{operation} returned an empty response.");
	}

	private static void EnsureDeleteSucceeded(DeleteWorkspaceItemsResponse response, string schemaName,
		string packageName) {
		if (response.Success && response.RowsAffected > 0) {
			return;
		}

		string message = string.IsNullOrWhiteSpace(response.ErrorInfo?.Message)
			? $"Failed to delete schema '{schemaName}' from package '{packageName}'."
			: response.ErrorInfo.Message;
		throw new InvalidOperationException(message);
	}
}

internal sealed class SysSchemaRemoteRecord {
	public Guid Id { get; set; }
	public Guid UId { get; set; }
	public string ManagerName { get; set; }
	public Guid PackageUId { get; set; }
	public string PackageName { get; set; }
}

internal sealed class WorkspaceItemsResponse {
	[JsonPropertyName("items")]
	public List<WorkspaceExplorerItemDto> Items { get; set; }
}

internal sealed class DeleteWorkspaceItemsResponse : BaseResponse {
	[JsonPropertyName("rowsAffected")]
	public int RowsAffected { get; set; }
}

internal sealed class WorkspaceExplorerItemDto {
	[JsonPropertyName("id")]
	public Guid Id { get; set; }

	[JsonPropertyName("uId")]
	public Guid UId { get; set; }

	[JsonPropertyName("name")]
	public string Name { get; set; }

	[JsonPropertyName("title")]
	public string Title { get; set; }

	[JsonPropertyName("packageUId")]
	public Guid PackageUId { get; set; }

	[JsonPropertyName("packageName")]
	public string PackageName { get; set; }

	[JsonPropertyName("type")]
	public int Type { get; set; }

	[JsonPropertyName("modifiedOn")]
	public string ModifiedOn { get; set; }

	[JsonPropertyName("isChanged")]
	public bool IsChanged { get; set; }

	[JsonPropertyName("isLocked")]
	public bool IsLocked { get; set; }

	[JsonPropertyName("isReadOnly")]
	public bool IsReadOnly { get; set; }
}
