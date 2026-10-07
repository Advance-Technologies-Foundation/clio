using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using Clio.Common.Responses;
using Clio.Package;
using Clio.Workspaces;
using CommandLine;

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

	[System.Text.Json.Serialization.JsonPropertyName("packageName")]
	public string PackageName { get; set; }

	/// <summary>
	/// Platform workspace item type (<c>WorkspaceItemType</c>) of the deleted item, as GetWorkspaceItems reported it.
	/// </summary>
	[System.Text.Json.Serialization.JsonPropertyName("itemType")]
	public int ItemType { get; set; }

	[System.Text.Json.Serialization.JsonPropertyName("error")]
	public string Error { get; set; }
}

public class DeleteSchemaCommand : RemoteCommand<DeleteSchemaOptions> {

	private static readonly JsonSerializerOptions SerializerOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IJsonConverter _jsonConverter;
	private readonly IFileSystem _fileSystem;
	private readonly IDeletedItemFileCleaner _deletedItemFileCleaner;

	/// <summary>
	/// Initializes a new instance of the <see cref="DeleteSchemaCommand"/> class.
	/// </summary>
	/// <param name="applicationClient">Client of the target environment.</param>
	/// <param name="settings">Target environment settings.</param>
	/// <param name="serviceUrlBuilder">Builds the WorkspaceExplorerService routes.</param>
	/// <param name="workspacePathBuilder">Resolves the local workspace in workspace mode.</param>
	/// <param name="jsonConverter">Reads the workspace settings.</param>
	/// <param name="fileSystem">Checks the workspace folder.</param>
	/// <param name="deletedItemFileCleaner">
	/// Removes the deleted item's folders from the package folder when the environment is in file system mode.
	/// </param>
	public DeleteSchemaCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder, IWorkspacePathBuilder workspacePathBuilder,
		IJsonConverter jsonConverter, IFileSystem fileSystem, IDeletedItemFileCleaner deletedItemFileCleaner)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
		_workspacePathBuilder = workspacePathBuilder;
		_jsonConverter = jsonConverter;
		_fileSystem = fileSystem;
		_deletedItemFileCleaner = deletedItemFileCleaner;
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
				$"Deleted schema '{remoteResponse.SchemaName}' (uId={remoteResponse.SchemaUId}) from package '{remoteResponse.PackageName}'."
				+ DescribeRetainedDatabaseObjects(remoteResponse.ItemType));
			ReportPackageFiles(options, remoteResponse.PackageName, remoteResponse.SchemaName, remoteResponse.ItemType);
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
			MaxAttempts, DelaySec);

		DeleteWorkspaceItemsResponse deleteResponse =
			Deserialize<DeleteWorkspaceItemsResponse>(deleteResponseJson, "Delete");
		EnsureDeleteSucceeded(deleteResponse, schemaItem.Name, schemaItem.PackageName);
		Logger.WriteInfo($"Deleted schema '{schemaItem.Name}' from package '{schemaItem.PackageName}'."
			+ DescribeRetainedDatabaseObjects(schemaItem.Type));
		ReportPackageFiles(options, schemaItem.PackageName, schemaItem.Name, schemaItem.Type);
	}

	/// <summary>
	/// Names what the platform delete leaves in the database, so a caller does not take the delete as complete.
	/// </summary>
	private static string DescribeRetainedDatabaseObjects(int itemType) =>
		itemType == PackageItemFolders.EntitySchemaType
			? " The database table, its columns and its data are not dropped: delete-schema removes the schema "
				+ "metadata only."
			: string.Empty;

	/// <summary>
	/// In file system mode the platform delete leaves the item's folders in the package folder, and the next
	/// pkg-to-db registers the item again. Removes them, or names every folder left behind.
	/// </summary>
	private void ReportPackageFiles(DeleteSchemaOptions options, string packageName, string itemName, int itemType) {
		DeletedItemFileCleanupResult result = _deletedItemFileCleaner.Clean(new DeletedItemFileCleanupRequest(
			ResolveEnvironmentName(options), options.EnvironmentPath, packageName, itemName, itemType));
		string expectedFolders = result.ExpectedFolders.Count > 0
			? string.Join(", ", result.ExpectedFolders)
			: "every folder that holds it";
		string problem = result.Problem?.TrimEnd('.', ' ');
		string consequence = $"otherwise the next pkg-to-db registers '{itemName}' again";
		switch (result.Status) {
			case DeletedItemFileCleanupStatus.FileSystemModeUnknown:
				Logger.WriteWarning(
					$"Could not check whether the environment is in file system mode: {problem}. If it is, "
					+ $"remove these folders of '{itemName}' from package '{packageName}', {consequence}: "
					+ $"{expectedFolders}.");
				return;
			case DeletedItemFileCleanupStatus.NotCleaned:
				Logger.WriteWarning(
					$"The environment is in file system mode, but clio did not remove the files of '{itemName}' "
					+ $"from package '{packageName}': {problem}. Remove these folders from the package (in the "
					+ $"linked repository, if the package is linked) by hand, {consequence}: {expectedFolders}.");
				return;
			case DeletedItemFileCleanupStatus.Cleaned:
				ReportCleanedPackageFiles(result, itemName, consequence);
				return;
			default:
				// FileSystemModeOff: the site does not read the package folder, so its files are not the source of truth.
				return;
		}
	}

	private void ReportCleanedPackageFiles(DeletedItemFileCleanupResult result, string itemName, string consequence) {
		if (result.RemovedFolders.Count > 0) {
			Logger.WriteInfo(
				$"Removed from package folder '{result.PackageFolderPath}': {string.Join(", ", result.RemovedFolders)}.");
		}
		if (result.RemainingFolders.Count > 0) {
			Logger.WriteWarning(
				$"Could not remove from package folder '{result.PackageFolderPath}': "
				+ $"{string.Join(", ", result.RemainingFolders)}. Remove them by hand, {consequence}.");
		}
		if (result.RemovedFolders.Count == 0 && result.RemainingFolders.Count == 0) {
			Logger.WriteInfo($"No files of '{itemName}' were found in package folder '{result.PackageFolderPath}'.");
		}
	}

	/// <summary>
	/// The registered environment the command runs against. A bare <c>--uri</c> call has none: the settings then
	/// carry the active environment's name, which is not the site the call targets.
	/// </summary>
	private string ResolveEnvironmentName(DeleteSchemaOptions options) {
		if (!string.IsNullOrWhiteSpace(options.Environment)) {
			return options.Environment.Trim();
		}
		return string.IsNullOrWhiteSpace(options.Uri) ? EnvironmentSettings?.EnvironmentName : null;
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
			// Use the same GetWorkspaceItems path as workspace-mode so the DTO carries
			// the platform-computed `type` (WorkspaceExplorerItemType, not ClientUnitSchemaType).
			// Without that, classic Creatio resolves the wrong SchemaManager and returns a
			// silent success:true rowsAffected:0 — see clio.tests/Command/DeleteSchemaRemoteCommandTests.
			string itemsUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems);
			string itemsJson = ApplicationClient.ExecutePostRequest(
				itemsUrl, string.Empty, RequestTimeout, MaxAttempts, DelaySec);
			WorkspaceItemsResponse itemsResponse =
				Deserialize<WorkspaceItemsResponse>(itemsJson, "GetWorkspaceItems");
			WorkspaceExplorerItemDto item = (itemsResponse.Items ?? [])
				.FirstOrDefault(x => string.Equals(x.Name, schemaName, StringComparison.OrdinalIgnoreCase));
			if (item is null) {
				response = new DeleteSchemaRemoteResponse {
					Success = false,
					Error = $"Schema '{schemaName}' not found in the target environment."
				};
				return false;
			}
			string deleteUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DeleteWorkspaceItem);
			string requestBody = JsonSerializer.Serialize(new[] { item });
			string responseJson = ApplicationClient.ExecutePostRequest(
				deleteUrl, requestBody, RequestTimeout, MaxAttempts, DelaySec);
			DeleteWorkspaceItemsResponse deleteResponse =
				Deserialize<DeleteWorkspaceItemsResponse>(responseJson, "Delete");
			if (!deleteResponse.Success || deleteResponse.RowsAffected <= 0) {
				string platformMessage = deleteResponse.ErrorInfo?.Message;
				string message = string.IsNullOrWhiteSpace(platformMessage)
					? $"Platform did not delete schema '{schemaName}' "
						+ $"(endpoint={deleteUrl}, success={deleteResponse.Success}, "
						+ $"rowsAffected={deleteResponse.RowsAffected}, "
						+ $"body={Truncate(responseJson, 500)})."
					: $"{platformMessage} (endpoint={deleteUrl})";
				response = new DeleteSchemaRemoteResponse {
					Success = false,
					SchemaName = schemaName,
					SchemaUId = item.UId.ToString(),
					PackageName = item.PackageName,
					Error = message
				};
				return false;
			}
			response = new DeleteSchemaRemoteResponse {
				Success = true,
				SchemaName = schemaName,
				SchemaUId = item.UId.ToString(),
				PackageName = item.PackageName,
				ItemType = item.Type
			};
			return true;
		}
		catch (Exception ex) {
			response = new DeleteSchemaRemoteResponse {
				Success = false,
				Error = $"[{ex.GetType().Name}] {ex.Message}"
			};
			return false;
		}
	}

	private static string Truncate(string value, int maxLength) {
		if (string.IsNullOrEmpty(value) || value.Length <= maxLength) {
			return value;
		}
		return value[..maxLength] + "…";
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
			MaxAttempts, DelaySec);

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
