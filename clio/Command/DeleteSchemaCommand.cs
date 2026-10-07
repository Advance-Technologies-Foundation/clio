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
	/// Workspace item type (<c>WorkspaceExplorerItemType</c>) of the deleted item, as GetWorkspaceItems reported it.
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
			RemoveDeletedItemFiles(options, remoteResponse.PackageName, remoteResponse.SchemaName, remoteResponse.ItemType);
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
		RemoveDeletedItemFiles(options, schemaItem.PackageName, schemaItem.Name, schemaItem.Type);
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
	/// <remarks>
	/// Runs after the database delete, which cannot be undone, so nothing here may fail the command: every problem
	/// becomes a warning, and a non-zero exit code would only invite a retry that fails with "not found".
	/// </remarks>
	private void RemoveDeletedItemFiles(DeleteSchemaOptions options, string packageName, string itemName,
		int itemType) {
		IReadOnlyList<string> expected = PackageItemFolders.GetRules(itemType, itemName)
			.Select(rule => rule.Describe()).ToList();
		string expectedFolders = expected.Count > 0 ? string.Join(", ", expected) : "every folder that holds it";
		string consequence = $"otherwise the next pkg-to-db registers '{itemName}' again and the next "
			+ $"configuration publish can fail with 'Item with name \"{itemName}\" not found'";
		bool? isFileDesignMode = ReadFileDesignMode(out string probeProblem);
		if (isFileDesignMode == false) {
			// The site does not read the package folder, so its files are not the source of truth.
			return;
		}
		if (isFileDesignMode is null) {
			Logger.WriteWarning(
				$"Could not check whether the environment is in file system mode: {TrimSentence(probeProblem)}. If it "
				+ $"is, remove these folders of '{itemName}' from package '{packageName}', {consequence}: "
				+ $"{expectedFolders}.");
			return;
		}
		DeletedItemFileCleanupResult result;
		try {
			result = _deletedItemFileCleaner.Clean(new DeletedItemFileCleanupRequest(ResolveEnvironmentName(options),
				options.EnvironmentPath, packageName, itemName, itemType));
		}
		catch (Exception exception) {
			// The cleaner reports file system failures itself; anything else must still end as a warning.
			result = new DeletedItemFileCleanupResult(DeletedItemFileCleanupStatus.NotCleaned, null, expected, [], [],
				exception.Message);
		}
		if (result.Status == DeletedItemFileCleanupStatus.Cleaned) {
			ReportCleanedPackageFiles(result, itemName, expectedFolders, consequence);
			return;
		}
		Logger.WriteWarning(
			$"The environment is in file system mode, but clio did not remove the files of '{itemName}' from package "
			+ $"'{packageName}': {TrimSentence(result.Problem)}. Remove these folders from the package (in the linked "
			+ $"repository, if the package is linked) by hand, {consequence}: {expectedFolders}.");
	}

	/// <summary>
	/// Reads the site's file design mode through the same connection, credentials and timeouts as the delete.
	/// </summary>
	/// <returns><c>true</c> or <c>false</c> when the site answered; <c>null</c> with the reason otherwise.</returns>
	private bool? ReadFileDesignMode(out string problem) {
		try {
			string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetIsFileDesignMode);
			string json = ApplicationClient.ExecutePostRequest(url, string.Empty, RequestTimeout, MaxAttempts,
				DelaySec);
			BoolResponse response = Deserialize<BoolResponse>(json, "GetIsFileDesignMode");
			if (!response.Success) {
				problem = response.ErrorInfo?.Message ?? "GetIsFileDesignMode reported a failure";
				return null;
			}
			problem = null;
			return response.Value;
		}
		catch (Exception exception) {
			// Transport, authentication or an unexpected payload all mean the same thing here: the mode is unknown.
			problem = $"GetIsFileDesignMode failed: {exception.Message}";
			return null;
		}
	}

	private static string TrimSentence(string text) => text?.TrimEnd('.', ' ');

	private void ReportCleanedPackageFiles(DeletedItemFileCleanupResult result, string itemName,
		string expectedFolders, string consequence) {
		if (result.RemovedFolders.Count > 0) {
			Logger.WriteInfo(
				$"Removed from package folder '{result.PackageFolderPath}': {string.Join(", ", result.RemovedFolders)}.");
		}
		if (result.RemainingFolders.Count > 0) {
			Logger.WriteWarning(
				$"Could not remove from package folder '{result.PackageFolderPath}': "
				+ $"{string.Join(", ", result.RemainingFolders)}. Remove them by hand, {consequence}.");
		}
		if (result.KeptFolders is { Count: > 0 }) {
			Logger.WriteInfo($"Kept in package folder '{result.PackageFolderPath}' because they are still in use: "
				+ $"{string.Join(", ", result.KeptFolders)}.");
		}
		if (result.RemovedFolders.Count == 0 && result.RemainingFolders.Count == 0
			&& result.KeptFolders is not { Count: > 0 }) {
			Logger.WriteInfo($"No folders of '{itemName}' were found in package folder '{result.PackageFolderPath}' "
				+ $"(searched: {expectedFolders}).");
		}
	}

	/// <summary>
	/// The registered environment the command runs against. A call with <c>--uri</c> has none, even next to
	/// <c>-e</c>: the URI replaces the registered site, so that environment's file system mode and site folder
	/// would describe a different site than the one the schema was deleted from.
	/// </summary>
	private string ResolveEnvironmentName(DeleteSchemaOptions options) {
		if (!string.IsNullOrWhiteSpace(options.Uri)) {
			return null;
		}
		return string.IsNullOrWhiteSpace(options.Environment)
			? EnvironmentSettings?.EnvironmentName
			: options.Environment.Trim();
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
