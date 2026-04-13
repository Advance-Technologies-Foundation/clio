using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

[Verb("delete-entity-schema", Aliases = ["delete-schema"], HelpText = "Delete a schema that belongs to a package in the current workspace")]
public class DeleteSchemaOptions : RemoteCommandOptions {

	[Value(0, MetaName = "SchemaName", Required = true, HelpText = "Schema name")]
	public string SchemaName { get; set; }

	[Option("workspace-path", Required = false, Hidden = true,
		HelpText = "Workspace path override. Intended for MCP usage.")]
	public string WorkspacePath { get; set; }
}

public class DeleteSchemaCommand : RemoteCommand<DeleteSchemaOptions> {
	private static readonly JsonSerializerOptions SerializerOptions = new() {
		PropertyNameCaseInsensitive = true
	};
	private static readonly HashSet<int> SchemaWorkspaceItemTypes = [
		3,
		8
	];

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
		ConfigureWorkspace(options);
		EnsureWorkspace();
		string schemaName = options.SchemaName?.Trim();
		if (string.IsNullOrWhiteSpace(schemaName)) {
			throw new InvalidOperationException("Schema name cannot be empty.");
		}

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
				&& SchemaWorkspaceItemTypes.Contains(item.Type)
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
