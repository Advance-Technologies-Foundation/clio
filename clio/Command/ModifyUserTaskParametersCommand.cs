using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Common;
using Clio.Package;
using Clio.Workspaces;
using CommandLine;

namespace Clio.Command;

/// <summary>
/// Defines CLI options for modifying parameters on an existing workspace user task.
/// </summary>
[Verb("modify-user-task-parameters",
	HelpText = "Add or remove parameters on an existing user task that belongs to the current workspace")]
public class ModifyUserTaskParametersOptions : RemoteCommandOptions {

	/// <summary>
	/// Gets or sets the existing user task schema name.
	/// </summary>
	[Value(0, MetaName = "UserTaskName", Required = true, HelpText = "Existing user task schema name")]
	public string UserTaskName { get; set; }

	/// <summary>
	/// Gets or sets parameter definitions to add.
	/// </summary>
	[Option("add-parameter", Required = false, Separator = '|',
		HelpText = "Parameter definition in 'code=<name>;title=<caption>;type=<type>[;direction=<In|Out|Variable|0|1|2>][;required=true][;resulting=true][;serializable=true][;copyValue=true][;lazyLoad=true][;containsPerformerId=true]' format. Separate multiple values with '|'")]
	public IEnumerable<string> AddParameters { get; set; }

	/// <summary>
	/// Gets or sets parameter names to remove.
	/// </summary>
	[Option("remove-parameter", Required = false, Separator = '|',
		HelpText = "Parameter name to remove. Separate multiple values with '|'")]
	public IEnumerable<string> RemoveParameters { get; set; }

	/// <summary>
	/// Gets or sets direction updates for existing parameters.
	/// </summary>
	[Option("set-direction", Required = false, Separator = '|',
		HelpText = "Set direction for an existing parameter in '<name>=<In|Out|Variable|0|1|2>' format. Separate multiple values with '|'")]
	public IEnumerable<string> SetDirections { get; set; }

	/// <summary>
	/// Gets or sets the culture used for added parameter titles.
	/// </summary>
	[Option("culture", Required = false, Default = "en-US",
		HelpText = "Culture for added parameter titles")]
	public string Culture { get; set; }

	/// <summary>
	/// Gets or sets the workspace root override used by MCP tools.
	/// </summary>
	[Option("workspace-path", Required = false, Hidden = true,
		HelpText = "Workspace path override. Intended for MCP usage.")]
	public string WorkspacePath { get; set; }
}

/// <summary>
/// Adds or removes parameters on an existing workspace-owned user task schema.
/// </summary>
public class ModifyUserTaskParametersCommand : RemoteCommand<ModifyUserTaskParametersOptions> {
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IJsonConverter _jsonConverter;
	private readonly IFileSystem _fileSystem;
	private readonly IFileDesignModePackages _fileDesignModePackages;
	private readonly IUserTaskMetadataDirectionApplier _userTaskMetadataDirectionApplier;

	/// <summary>
	/// Initializes a new instance of the <see cref="ModifyUserTaskParametersCommand"/> class.
	/// </summary>
	public ModifyUserTaskParametersCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder, IWorkspacePathBuilder workspacePathBuilder,
		IJsonConverter jsonConverter, IFileSystem fileSystem, IFileDesignModePackages fileDesignModePackages,
		IUserTaskMetadataDirectionApplier userTaskMetadataDirectionApplier)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
		_workspacePathBuilder = workspacePathBuilder;
		_jsonConverter = jsonConverter;
		_fileSystem = fileSystem;
		_fileDesignModePackages = fileDesignModePackages;
		_userTaskMetadataDirectionApplier = userTaskMetadataDirectionApplier;
	}

	/// <inheritdoc />
	protected override void ExecuteRemoteCommand(ModifyUserTaskParametersOptions options) {
		ConfigureWorkspace(options);
		EnsureWorkspace();
		string userTaskName = options.UserTaskName?.Trim();
		if (string.IsNullOrWhiteSpace(userTaskName)) {
			throw new InvalidOperationException("User task name cannot be empty.");
		}

		List<string> parameterNamesToRemove = NormalizeRemoveParameterNames(options.RemoveParameters);
		List<UserTaskParameterDto> parametersToAdd = UserTaskSchemaSupport.BuildParameters(options.Culture, options.AddParameters);
		Dictionary<string, int> explicitAddedParameterDirections = UserTaskSchemaSupport
			.ExtractExplicitDirections(options.AddParameters);
		Dictionary<string, int> parameterDirectionsToUpdate = NormalizeDirectionUpdates(options.SetDirections);
		if (parameterNamesToRemove.Count == 0 && parametersToAdd.Count == 0 && parameterDirectionsToUpdate.Count == 0) {
			throw new InvalidOperationException(
				"Specify at least one `--add-parameter`, `--remove-parameter`, or `--set-direction` operation.");
		}

		HashSet<string> workspacePackages = GetWorkspacePackages();
		WorkspaceExplorerItemDto schemaItem = FindWorkspaceUserTaskItem(userTaskName, workspacePackages);
		ProcessUserTaskDesignSchemaDto schema = LoadSchema(schemaItem);

		ApplyParameterChanges(schema, parametersToAdd, parameterNamesToRemove, parameterDirectionsToUpdate);

		string saveSchemaUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.SaveUserTaskSchema);
		string saveRequestBody = JsonSerializer.Serialize(schema);
		string saveResponseJson = ApplicationClient.ExecutePostRequest(saveSchemaUrl, saveRequestBody, RequestTimeout,
			RetryCount, DelaySec);
		SaveDesignItemDesignerResponse saveResponse = UserTaskSchemaSupport
			.Deserialize<SaveDesignItemDesignerResponse>(saveResponseJson, "SaveSchema");
		UserTaskSchemaSupport.EnsureSaveSucceeded(saveResponse);
		Logger.WriteInfo($"Updated user task schema '{schema.Name}' ({saveResponse.SchemaUId}).");
		BuildPackage(schemaItem.PackageName);
		ApplyParameterDirectionMetadataIfNeeded(schemaItem.PackageName, schema.Name,
			MergeDirectionUpdates(explicitAddedParameterDirections, parameterDirectionsToUpdate));
	}

	private void ConfigureWorkspace(ModifyUserTaskParametersOptions options) {
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

	private WorkspaceExplorerItemDto FindWorkspaceUserTaskItem(string userTaskName, HashSet<string> workspacePackages) {
		string getWorkspaceItemsUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetWorkspaceItems);
		string responseJson = ApplicationClient.ExecutePostRequest(getWorkspaceItemsUrl, string.Empty, RequestTimeout,
			RetryCount, DelaySec);
		WorkspaceItemsResponse response =
			UserTaskSchemaSupport.Deserialize<WorkspaceItemsResponse>(responseJson, "GetWorkspaceItems");

		List<WorkspaceExplorerItemDto> matches = (response.Items ?? [])
			.Where(item => workspacePackages.Contains(item.PackageName)
				&& string.Equals(item.Name, userTaskName, StringComparison.OrdinalIgnoreCase))
			.ToList();

		if (matches.Count == 0) {
			throw new InvalidOperationException(
				$"User task '{userTaskName}' is not part of the current workspace.");
		}

		if (matches.Count > 1) {
			string packages = string.Join(", ", matches.Select(item => item.PackageName).Distinct(StringComparer.OrdinalIgnoreCase));
			throw new InvalidOperationException(
				$"User task '{userTaskName}' exists in multiple workspace packages: {packages}. Update is ambiguous.");
		}

		return matches[0];
	}

	private ProcessUserTaskDesignSchemaDto LoadSchema(WorkspaceExplorerItemDto schemaItem) {
		string getSchemaUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetUserTaskSchema);
		string requestBody = JsonSerializer.Serialize(new GetSchemaRequestDto {
			SchemaUId = schemaItem.UId,
			UseFullHierarchy = false,
			Cultures = []
		});
		string responseJson = ApplicationClient.ExecutePostRequest(getSchemaUrl, requestBody, RequestTimeout,
			RetryCount, DelaySec);
		DesignerResponse<ProcessUserTaskDesignSchemaDto> response = UserTaskSchemaSupport
			.Deserialize<DesignerResponse<ProcessUserTaskDesignSchemaDto>>(responseJson, "GetSchema");
		UserTaskSchemaSupport.EnsureResponseSucceeded(response, "GetSchema");
		return response.Schema
			?? throw new InvalidOperationException($"GetSchema did not return schema payload for '{schemaItem.Name}'.");
	}

	private static List<string> NormalizeRemoveParameterNames(IEnumerable<string> removeParameters) {
		var normalizedNames = new List<string>();
		var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string parameterName in removeParameters ?? []) {
			string normalizedName = parameterName?.Trim();
			if (string.IsNullOrWhiteSpace(normalizedName)) {
				throw new InvalidOperationException("Remove parameter name cannot be empty.");
			}

			if (!knownNames.Add(normalizedName)) {
				throw new InvalidOperationException(
					$"Parameter '{normalizedName}' is listed more than once for removal.");
			}

			normalizedNames.Add(normalizedName);
		}

		return normalizedNames;
	}

	private static Dictionary<string, int> NormalizeDirectionUpdates(IEnumerable<string> setDirections) {
		var directionUpdates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (string directionUpdate in setDirections ?? []) {
			if (string.IsNullOrWhiteSpace(directionUpdate)) {
				throw new InvalidOperationException("Set direction value cannot be empty.");
			}

			int separatorIndex = directionUpdate.IndexOf('=');
			if (separatorIndex <= 0) {
				separatorIndex = directionUpdate.IndexOf(':');
			}

			if (separatorIndex <= 0) {
				throw new InvalidOperationException(
					$"Set direction value '{directionUpdate}' must be in '<name>=<direction>' format.");
			}

			string parameterName = directionUpdate[..separatorIndex].Trim();
			string directionValue = directionUpdate[(separatorIndex + 1)..].Trim();
			if (string.IsNullOrWhiteSpace(parameterName)) {
				throw new InvalidOperationException(
					$"Set direction value '{directionUpdate}' must include a parameter name.");
			}

			int direction = UserTaskSchemaSupport.ParseDirection(directionValue, directionUpdate);
			if (!directionUpdates.TryAdd(parameterName, direction)) {
				throw new InvalidOperationException(
					$"Parameter '{parameterName}' is listed more than once for direction updates.");
			}
		}

		return directionUpdates;
	}

	private static void ApplyParameterChanges(ProcessUserTaskDesignSchemaDto schema, List<UserTaskParameterDto> parametersToAdd,
		List<string> parameterNamesToRemove, Dictionary<string, int> parameterDirectionsToUpdate) {
		schema.Parameters ??= [];

		foreach (string parameterNameToRemove in parameterNamesToRemove) {
			UserTaskParameterDto existingParameter = schema.Parameters.FirstOrDefault(parameter =>
				string.Equals(parameter.Name, parameterNameToRemove, StringComparison.OrdinalIgnoreCase));
			if (existingParameter is null) {
				throw new InvalidOperationException(
					$"Parameter '{parameterNameToRemove}' does not exist on user task '{schema.Name}'.");
			}

			schema.Parameters.Remove(existingParameter);
		}

		foreach (UserTaskParameterDto parameterToAdd in parametersToAdd) {
			bool alreadyExists = schema.Parameters.Any(parameter =>
				string.Equals(parameter.Name, parameterToAdd.Name, StringComparison.OrdinalIgnoreCase));
			if (alreadyExists) {
				throw new InvalidOperationException(
					$"Parameter '{parameterToAdd.Name}' already exists on user task '{schema.Name}'.");
			}

			schema.Parameters.Add(parameterToAdd);
		}

		foreach ((string parameterName, int direction) in parameterDirectionsToUpdate) {
			UserTaskParameterDto existingParameter = schema.Parameters.FirstOrDefault(parameter =>
				string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase));
			if (existingParameter is null) {
				throw new InvalidOperationException(
					$"Parameter '{parameterName}' does not exist on user task '{schema.Name}'.");
			}

			existingParameter.Direction = direction;
		}
	}

	private void ApplyParameterDirectionMetadataIfNeeded(string packageName, string schemaName,
		IReadOnlyDictionary<string, int> directionsByParameterName) {
		if (directionsByParameterName is null || directionsByParameterName.Count == 0) {
			return;
		}

		Logger.WriteInfo($"Applying direction metadata for {directionsByParameterName.Count} parameter(s) on '{schemaName}'...");
		_userTaskMetadataDirectionApplier.ApplyDirections(packageName, schemaName, directionsByParameterName);
		Logger.WriteInfo("Loading workspace packages to database to apply direction metadata changes...");
		_fileDesignModePackages.LoadPackagesToDb();
		BuildPackage(packageName);
	}

	private static Dictionary<string, int> MergeDirectionUpdates(
		IReadOnlyDictionary<string, int> explicitAddedParameterDirections,
		IReadOnlyDictionary<string, int> parameterDirectionsToUpdate) {
		var mergedDirections = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach ((string parameterName, int direction) in explicitAddedParameterDirections ?? new Dictionary<string, int>()) {
			mergedDirections[parameterName] = direction;
		}

		foreach ((string parameterName, int direction) in parameterDirectionsToUpdate ?? new Dictionary<string, int>()) {
			mergedDirections[parameterName] = direction;
		}

		return mergedDirections;
	}

	private void BuildPackage(string packageName) {
		string buildPackageUrl = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildPackage);
		string buildRequestBody = JsonSerializer.Serialize(new BuildPackageRequestDto {
			PackageName = packageName
		});
		Logger.WriteInfo($"Building package '{packageName}'...");
		ApplicationClient.ExecutePostRequest(buildPackageUrl, buildRequestBody, RequestTimeout, RetryCount, DelaySec);
	}
}
