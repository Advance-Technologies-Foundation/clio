using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clio.Common;
using Clio.Workspaces;
using IFileSystem = System.IO.Abstractions.IFileSystem;

namespace Clio.Command;

/// <summary>
/// Applies user task parameter direction values directly to workspace metadata files.
/// </summary>
public interface IUserTaskMetadataDirectionApplier {
	/// <summary>
	/// Updates parameter direction values in the workspace metadata for the specified user task schema.
	/// </summary>
	/// <param name="packageName">Workspace package name that owns the schema.</param>
	/// <param name="schemaName">User task schema name.</param>
	/// <param name="directionsByParameterName">Direction values keyed by parameter name.</param>
	void ApplyDirections(string packageName, string schemaName, IReadOnlyDictionary<string, int> directionsByParameterName);
}

/// <summary>
/// Updates <c>L12</c> values for user task parameters in workspace metadata files.
/// </summary>
public class UserTaskMetadataDirectionApplier : IUserTaskMetadataDirectionApplier {
	private readonly IWorkspacePathBuilder _workspacePathBuilder;
	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="UserTaskMetadataDirectionApplier"/> class.
	/// </summary>
	/// <param name="workspacePathBuilder">Workspace path builder.</param>
	/// <param name="fileSystem">Abstracted file system.</param>
	public UserTaskMetadataDirectionApplier(IWorkspacePathBuilder workspacePathBuilder, IFileSystem fileSystem) {
		_workspacePathBuilder = workspacePathBuilder;
		_fileSystem = fileSystem;
	}

	/// <inheritdoc />
	public void ApplyDirections(string packageName, string schemaName,
		IReadOnlyDictionary<string, int> directionsByParameterName) {
		if (directionsByParameterName is null || directionsByParameterName.Count == 0) {
			return;
		}

		string metadataPath = BuildMetadataPath(packageName, schemaName);
		if (!_fileSystem.File.Exists(metadataPath)) {
			throw new InvalidOperationException(
				$"User task metadata file was not found at '{metadataPath}'.");
		}

		JsonObject root = JsonNode.Parse(_fileSystem.File.ReadAllText(metadataPath))?.AsObject()
			?? throw new InvalidOperationException(
				$"User task metadata file '{metadataPath}' is empty or invalid.");
		JsonArray parameters = root["MetaData"]?["Schema"]?["FJ1"]?.AsArray()
			?? throw new InvalidOperationException(
				$"User task metadata file '{metadataPath}' does not contain schema parameter metadata.");

		foreach ((string parameterName, int direction) in directionsByParameterName) {
			JsonObject parameter = parameters
				.OfType<JsonNode>()
				.Select(node => node as JsonObject)
				.FirstOrDefault(node =>
					string.Equals(node?["A2"]?.GetValue<string>(), parameterName, StringComparison.OrdinalIgnoreCase));

			if (parameter is null) {
				throw new InvalidOperationException(
					$"Parameter '{parameterName}' was not found in metadata for user task '{schemaName}'.");
			}

			parameter["L12"] = direction;
		}

		_fileSystem.File.WriteAllText(metadataPath, root.ToJsonString(new JsonSerializerOptions {
			WriteIndented = true
		}));
	}

	private string BuildMetadataPath(string packageName, string schemaName) {
		string packagePath = _workspacePathBuilder.BuildPackagePath(packageName);
		return _fileSystem.Path.Combine(packagePath, "Schemas", schemaName, "metadata.json");
	}
}
