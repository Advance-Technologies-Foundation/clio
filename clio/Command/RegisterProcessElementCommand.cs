using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

/// <summary>Local package inputs for custom process element registration.</summary>
[Verb("register-process-element", HelpText = "Generate package-owned PostgreSQL and SQL Server process element registration scripts")]
public sealed class RegisterProcessElementOptions : EnvironmentOptions {
	/// <summary>Gets or sets the explicit clio workspace directory.</summary>
	[Option("workspace-path", Required = true, HelpText = "Local clio workspace directory")]
	public string WorkspacePath { get; set; }
	/// <summary>Gets or sets the package that owns the existing task.</summary>
	[Option("package-name", Required = true, HelpText = "Workspace package owning the task")]
	public string PackageName { get; set; }
	/// <summary>Gets or sets the existing user-task schema UId.</summary>
	[Option("user-task-uid", Required = true, HelpText = "Existing user-task schema UId")]
	public Guid UserTaskUId { get; set; }
	/// <summary>Gets or sets the toolbox caption inserted on first installation.</summary>
	[Option("caption", Required = true, HelpText = "Initial process element caption")]
	public string Caption { get; set; }
	internal override bool RequiredEnvironment => false;
}

/// <summary>Generates native SQL script artifacts without connecting to Creatio.</summary>
public interface IProcessElementRegistration {
	/// <summary>Validates task ownership and creates missing registration artifacts, preserving matching existing files.</summary>
	/// <param name="options">Workspace, package, task identity and caption.</param>
	/// <returns>The two SQL script paths.</returns>
	IReadOnlyList<string> Generate(RegisterProcessElementOptions options);
}

/// <summary>Registers an existing user task through deployable package SQL artifacts.</summary>
public sealed class RegisterProcessElementCommand(IProcessElementRegistration registration, ILogger logger)
	: Command<RegisterProcessElementOptions> {
	/// <inheritdoc />
	public override int Execute(RegisterProcessElementOptions options) {
		try {
			foreach (string path in registration.Generate(options)) {
				logger.WriteInfo($"Registration script ready: {path}");
			}
			logger.WriteInfo("Install the package with push-pkg or push-workspace to apply registration. No environment was changed.");
			return 0;
		} catch (Exception exception) {
			logger.WriteError(exception.Message);
			return 1;
		}
	}
}

/// <summary>Produces duplicate-safe registration SQL and native installation descriptors.</summary>
public sealed class ProcessElementRegistration(System.IO.Abstractions.IFileSystem files) : IProcessElementRegistration {
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	/// <inheritdoc />
	public IReadOnlyList<string> Generate(RegisterProcessElementOptions options) {
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.WorkspacePath) || string.IsNullOrWhiteSpace(options.PackageName)
			|| !Regex.IsMatch(options.PackageName, @"\A[A-Za-z_][A-Za-z0-9_]*\z")
			|| options.UserTaskUId == Guid.Empty || string.IsNullOrWhiteSpace(options.Caption)
			|| options.Caption.Contains('\0')) {
			throw new ArgumentException("A workspace path, package name, nonempty user-task UId and caption are required.");
		}
		string workspace = files.Path.GetFullPath(options.WorkspacePath);
		JsonNode settings = Read(files.Path.Combine(workspace, ".clio", "workspaceSettings.json"));
		if (settings["Packages"] is not JsonArray packages
			|| !packages.Any(value => value?.GetValue<string>() == options.PackageName)) {
			throw new InvalidOperationException("The package must belong to the selected clio workspace.");
		}
		string packagePath = files.Path.Combine(workspace, "packages", options.PackageName);
		JsonNode packageDescriptor = Read(files.Path.Combine(packagePath, "descriptor.json"))["Descriptor"];
		if (packageDescriptor?["Name"]?.GetValue<string>() != options.PackageName
			|| !Guid.TryParse(packageDescriptor?["UId"]?.GetValue<string>(), out Guid packageUId)
			|| packageUId == Guid.Empty) {
			throw new InvalidOperationException("The package descriptor name or UId does not match the requested package.");
		}
		ValidateTask(packagePath, options.UserTaskUId, packageUId);
		List<(string Path, string Text)> pending = [];
		List<string> scriptPaths = [];
		foreach ((string suffix, int engine) in new[] { ("PostgreSql", 2), ("MsSql", 0) }) {
			string name = $"UsrRegisterTask{options.UserTaskUId:N}{suffix}";
			string directory = files.Path.Combine(packagePath, "SqlScripts", name);
			string scriptPath = files.Path.Combine(directory, name + ".sql");
			string descriptorPath = files.Path.Combine(directory, "descriptor.json");
			string sql = BuildSql(options.UserTaskUId, packageUId, options.Caption, engine);
			ValidateExistingScript(scriptPath, sql);
			if (files.File.Exists(descriptorPath)) {
				JsonNode descriptor = Read(descriptorPath)["SqlScript"];
				if (descriptor?["Name"]?.GetValue<string>() != name
					|| descriptor?["DBEngineType"]?.GetValue<int>() != engine
					|| descriptor?["InstallType"]?.GetValue<int>() != 1
					|| !Guid.TryParse(descriptor?["UId"]?.GetValue<string>(), out Guid uid) || uid == Guid.Empty) {
					throw new InvalidOperationException($"Existing registration descriptor conflicts with the requested task: {descriptorPath}");
				}
			} else {
				pending.Add((descriptorPath, JsonSerializer.Serialize(new {
					SqlScript = new { UId = Guid.NewGuid(), Name = name, DBEngineType = engine, InstallType = 1,
						ModifiedOnUtc = $"/Date({DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()})/" }
				}, JsonOptions)));
			}
			if (!files.File.Exists(scriptPath)) {
				pending.Add((scriptPath, sql));
			}
			scriptPaths.Add(scriptPath);
		}
		// Validate both dialects before writing anything; existing artifacts are never overwritten.
		foreach ((string path, string text) in pending) {
			files.Directory.CreateDirectory(files.Path.GetDirectoryName(path));
			using var stream = files.FileStream.New(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write);
			using var writer = new System.IO.StreamWriter(stream);
			writer.Write(text);
		}
		return scriptPaths;
	}

	private void ValidateTask(string packagePath, Guid taskUId, Guid packageUId) {
		string schemasPath = files.Path.Combine(packagePath, "Schemas");
		string match = null;
		foreach (string path in files.Directory.EnumerateFiles(schemasPath, "descriptor.json", System.IO.SearchOption.AllDirectories)) {
			JsonNode descriptor = Read(path)["Descriptor"];
			if (!Guid.TryParse(descriptor?["UId"]?.GetValue<string>(), out Guid uid) || uid != taskUId) {
				continue;
			}
			if (match is not null) {
				throw new InvalidOperationException("The user-task UId is duplicated in the package.");
			}
			match = path;
			JsonNode schema = Read(files.Path.Combine(files.Path.GetDirectoryName(path), "metadata.json"))["MetaData"]?["Schema"];
			if (descriptor?["ManagerName"]?.GetValue<string>() != "ProcessUserTaskSchemaManager"
				|| schema?["ManagerName"]?.GetValue<string>() != "ProcessUserTaskSchemaManager"
				|| schema?["A2"]?.GetValue<string>() != descriptor?["Name"]?.GetValue<string>()
				|| !Guid.TryParse(schema?["UId"]?.GetValue<string>(), out Guid metadataUid) || metadataUid != taskUId
				|| !Guid.TryParse(schema?["B6"]?.GetValue<string>(), out Guid owner) || owner != packageUId) {
				throw new InvalidOperationException("The schema must be a user task with matching descriptor, metadata and package identities.");
			}
		}
		if (match is null) {
			throw new InvalidOperationException("The requested user-task UId was not found in the workspace package.");
		}
	}

	private JsonNode Read(string path) => JsonNode.Parse(files.File.ReadAllText(path))
		?? throw new InvalidOperationException($"Invalid JSON artifact: {path}");

	private void ValidateExistingScript(string path, string expected) {
		if (files.File.Exists(path) && files.File.ReadAllText(path).Replace("\r\n", "\n") != expected.Replace("\r\n", "\n")) {
			throw new InvalidOperationException($"Existing registration SQL differs; it was preserved: {path}");
		}
	}

	private static string BuildSql(Guid task, Guid package, string caption, int engine) {
		string escapedCaption = caption.Replace("'", "''");
		string literal = engine == 2 ? "E'" + escapedCaption.Replace("\\", "\\\\") + "'" : "N'" + escapedCaption + "'";
		string Quote(string name) => engine == 2 ? "\"" + name + "\"" : "[" + name + "]";
		return $"""
			INSERT INTO {Quote("SysProcessUserTask")} ({Quote("SysUserTaskSchemaUId")}, {Quote("Caption")})
			SELECT s.{Quote("UId")}, {literal}
			FROM {Quote("SysSchema")} s
			INNER JOIN {Quote("SysPackage")} p ON p.{Quote("Id")} = s.{Quote("SysPackageId")}
			WHERE s.{Quote("UId")} = '{task:D}' AND p.{Quote("UId")} = '{package:D}'
			AND NOT EXISTS (SELECT 1 FROM {Quote("SysProcessUserTask")} t WHERE t.{Quote("SysUserTaskSchemaUId")} = s.{Quote("UId")});
			""";
	}
}
