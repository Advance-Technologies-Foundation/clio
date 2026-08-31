using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;

namespace Clio.Command;

/// <summary>Options for listing or reading files materialized for a compiled Creatio package.</summary>
[Verb("show-package-file-content", Aliases = ["show-files", "files"],
	HelpText = "List or read files materialized for a compiled Creatio package")]
[RequiresPackage("cliogate", "2.0.0.47",
	Hint = "Run 'clio install-gate -e <environment>' (or call the install-gate MCP tool) to install/update cliogate.")]
public class ShowPackageFileContentOptions : RemoteCommandOptions {
	/// <summary>Gets or sets the package name.</summary>
	[Option("package", Required = true, HelpText = "Package name")]
	public string PackageName { get; internal set; }

	/// <summary>Gets or sets the relative file path. Omit to list package files.</summary>
	[Option("file", Required = false, HelpText = "Relative file path. Omit to list package files")]
	public string FilePath { get; internal set; }
}

/// <summary>Structured result returned when package files are listed.</summary>
public sealed class PackageFileListResponse : EnvironmentProbeResponse {
	/// <summary>Gets the package that was inspected.</summary>
	[JsonPropertyName("package-name")]
	public string PackageName { get; init; }

	/// <summary>Gets the normalized package-relative file paths.</summary>
	[JsonPropertyName("files")]
	public IReadOnlyList<string> Files { get; init; } = [];

	/// <summary>Gets the number of returned paths.</summary>
	[JsonPropertyName("count")]
	public int Count { get; init; }
}

/// <summary>Structured result returned when one package file is read.</summary>
public sealed class PackageFileContentResponse : EnvironmentProbeResponse {
	/// <summary>Gets the package that was inspected.</summary>
	[JsonPropertyName("package-name")]
	public string PackageName { get; init; }

	/// <summary>Gets the requested package-relative file path.</summary>
	[JsonPropertyName("file-path")]
	public string FilePath { get; init; }

	/// <summary>Gets the requested file content.</summary>
	[JsonPropertyName("content")]
	public string Content { get; init; }

	/// <summary>Gets the requested file content length in characters.</summary>
	[JsonPropertyName("content-length")]
	public int ContentLength { get; init; }

	/// <summary>Gets the generated package project file path.</summary>
	[JsonPropertyName("project-file-path")]
	public string ProjectFilePath { get; init; }

	/// <summary>Gets the generated package project file content.</summary>
	[JsonPropertyName("project-content")]
	public string ProjectContent { get; init; }

	/// <summary>Gets the project file content length in characters.</summary>
	[JsonPropertyName("project-content-length")]
	public int ProjectContentLength { get; init; }

	/// <summary>Gets a non-fatal explanation when the generated project is unavailable.</summary>
	[JsonPropertyName("project-error")]
	[System.Text.Json.Serialization.JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string ProjectError { get; set; }
}

/// <summary>Lists and reads files materialized in a compiled package's <c>Files</c> directory.</summary>
public class ShowPackageFileContentCommand : RemoteCommand<ShowPackageFileContentOptions> {
	private const string ListRoute = "/rest/CreatioApiGateway/GetPackageFilesDirectoryContent";
	private const string ContentRoute = "/rest/CreatioApiGateway/GetPackageFileContent";
	private readonly ILogger _logger;

	/// <inheritdoc />
	public override HttpMethod HttpMethod => HttpMethod.Get;

	/// <summary>Initializes a new instance of the command.</summary>
	public ShowPackageFileContentCommand(IApplicationClient applicationClient,
		EnvironmentSettings environmentSettings, ILogger logger) : base(applicationClient, environmentSettings) {
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(ShowPackageFileContentOptions options) {
		if (string.IsNullOrWhiteSpace(options?.FilePath)) {
			if (!TryListPackageFiles(options, out PackageFileListResponse listResponse)) {
				_logger.WriteError(listResponse.Error ?? "Failed to list package files.");
				return 1;
			}
			foreach (string file in listResponse.Files) {
				_logger.WriteLine(file);
			}
			return 0;
		}

		if (!TryGetPackageFile(options, includeProjectFile: false, out PackageFileContentResponse contentResponse)) {
			_logger.WriteError(contentResponse.Error ?? "Failed to read the package file.");
			return 1;
		}
		_logger.WriteLine(contentResponse.Content);
		return 0;
	}

	/// <summary>Lists normalized package-relative file paths.</summary>
	public virtual bool TryListPackageFiles(ShowPackageFileContentOptions options,
		out PackageFileListResponse response) {
		ArgumentNullException.ThrowIfNull(options);
		ConfigureRequest(options);
		try {
			string packageName = RequirePackageName(options.PackageName);
			string rawResponse = ExecuteGet(ListRoute, packageName);
			IReadOnlyList<string> files = DeserializeResponse<string[]>(rawResponse, ListRoute)
				.Select(NormalizeReturnedPath)
				.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
				.ThenBy(path => path, StringComparer.Ordinal)
				.ToList();
			response = new PackageFileListResponse {
				Success = true, PackageName = packageName, Files = files, Count = files.Count
			};
			return true;
		} catch (Exception exception) {
			response = new PackageFileListResponse { Success = false, Error = exception.Message };
			return false;
		}
	}

	/// <summary>Reads a package file and the generated package project file.</summary>
	public virtual bool TryGetPackageFile(ShowPackageFileContentOptions options,
		out PackageFileContentResponse response) =>
		TryGetPackageFile(options, includeProjectFile: true, out response);

	internal bool TryGetPackageFile(ShowPackageFileContentOptions options, bool includeProjectFile,
		out PackageFileContentResponse response) {
		ArgumentNullException.ThrowIfNull(options);
		ConfigureRequest(options);
		try {
			string packageName = RequirePackageName(options.PackageName);
			string filePath = NormalizeRequestedPath(options.FilePath);
			string content = ReadFile(packageName, filePath);
			string projectFilePath = $"{packageName}.csproj";
			string projectContent = null;
			string projectError = null;
			if (includeProjectFile) {
				try {
					projectContent = string.Equals(filePath, projectFilePath, StringComparison.OrdinalIgnoreCase)
						? content
						: ReadFile(packageName, projectFilePath);
				} catch (Exception exception) {
					projectError = $"The generated project '{projectFilePath}' is unavailable: {exception.Message}";
				}
			}
			response = new PackageFileContentResponse {
				Success = true,
				PackageName = packageName,
				FilePath = filePath,
				Content = content,
				ContentLength = content.Length,
				ProjectFilePath = includeProjectFile ? projectFilePath : null,
				ProjectContent = projectContent,
				ProjectContentLength = projectContent?.Length ?? 0,
				ProjectError = projectError
			};
			return true;
		} catch (Exception exception) {
			response = new PackageFileContentResponse { Success = false, Error = exception.Message };
			return false;
		}
	}

	private string ReadFile(string packageName, string filePath) =>
		DeserializeResponse<string>(ExecuteGet(ContentRoute, packageName, filePath), ContentRoute);

	private string ExecuteGet(string route, string packageName, string filePath = null) {
		string query = $"?packageName={Uri.EscapeDataString(packageName)}";
		if (filePath is not null) {
			query += $"&filePath={Uri.EscapeDataString(filePath)}";
		}
		return ApplicationClient.ExecuteGetRequest(
			RootPath + route + query, RequestTimeout, MaxAttempts, DelaySec);
	}

	private void ConfigureRequest(ShowPackageFileContentOptions options) {
		RequestTimeout = options.TimeOut;
		MaxAttempts = options.MaxAttempts;
		DelaySec = options.RetryDelay;
	}

	private static T DeserializeResponse<T>(string rawResponse, string route) {
		if (string.IsNullOrWhiteSpace(rawResponse) ||
			rawResponse.TrimStart().StartsWith("<", StringComparison.Ordinal)) {
			throw new InvalidOperationException(
				$"ClioGate could not complete {route}. The package or file may not exist; inspect the Creatio Error.log. " +
				"If the installed gate artifacts are stale, run install-gate and retry.");
		}
		T result = JsonConvert.DeserializeObject<T>(rawResponse);
		return result ?? throw new InvalidOperationException($"ClioGate returned an empty JSON value from {route}.");
	}

	private static string RequirePackageName(string packageName) {
		if (string.IsNullOrWhiteSpace(packageName)) {
			throw new ArgumentException("Package name is required.", nameof(packageName));
		}
		return packageName.Trim();
	}

	private static string NormalizeRequestedPath(string filePath) {
		if (string.IsNullOrWhiteSpace(filePath)) {
			throw new ArgumentException("File path is required.", nameof(filePath));
		}
		string trimmed = filePath.Trim();
		if (Path.IsPathRooted(trimmed) || HasWindowsDrivePrefix(trimmed)) {
			throw new ArgumentException("File path must be relative and remain inside the package Files directory.",
				nameof(filePath));
		}
		string normalized = trimmed.Replace('\\', '/');
		if (normalized.Split('/').Any(segment => segment is "." or "..")) {
			throw new ArgumentException("File path must remain inside the package Files directory.", nameof(filePath));
		}
		return normalized;
	}

	private static bool HasWindowsDrivePrefix(string path) =>
		path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';

	private static string NormalizeReturnedPath(string filePath) =>
		(filePath ?? string.Empty).Replace('\\', '/').TrimStart('/');
}
