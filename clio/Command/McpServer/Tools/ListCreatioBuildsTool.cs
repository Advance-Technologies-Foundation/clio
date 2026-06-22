using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.UserEnvironment;
using ModelContextProtocol.Server;
using IFileSystem = System.IO.Abstractions.IFileSystem;
using IFileInfo = System.IO.Abstractions.IFileInfo;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface that lists the Creatio build archives available under the configured products folder.
/// </summary>
[McpServerToolType]
public sealed class ListCreatioBuildsTool {
	/// <summary>
	/// Stable MCP tool name for build discovery.
	/// </summary>
	internal const string ListCreatioBuildsToolName = "list-creatio-builds";

	/// <summary>
	/// Maximum number of build archives returned in a single response to keep the payload bounded.
	/// </summary>
	internal const int MaxBuilds = 200;

	private readonly ISettingsRepository _settingsRepository;
	private readonly IFileSystem _fileSystem;

	/// <summary>
	/// Initializes a new instance of the <see cref="ListCreatioBuildsTool"/> class.
	/// </summary>
	public ListCreatioBuildsTool(ISettingsRepository settingsRepository, IFileSystem fileSystem) {
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	}

	/// <summary>
	/// Lists Creatio build archives (.zip) discovered under the configured creatio-products folder so an
	/// agent can pick a deploy-creatio zipFile deterministically instead of globbing the filesystem.
	/// </summary>
	[McpServerTool(Name = ListCreatioBuildsToolName, ReadOnly = true, Destructive = false, Idempotent = true,
		OpenWorld = false)]
	[Description("""
				 Lists the Creatio build archives (.zip) available under the configured `creatio-products` folder.

				 Use this before `deploy-creatio` to discover a build and pass its full path as `zipFile`, instead
				 of globbing the filesystem. The response includes the resolved products folder and whether it exists,
				 so a stale or missing `creatio-products` configuration is surfaced explicitly.
				 """)]
	public ListCreatioBuildsResult ListCreatioBuilds() {
		string productsFolder = _settingsRepository.GetCreatioProductsFolder();
		if (string.IsNullOrWhiteSpace(productsFolder)) {
			return new ListCreatioBuildsResult(
				"products-folder-not-configured",
				productsFolder,
				false,
				"No 'creatio-products' folder is configured in clio appsettings.json. Set it to the directory that holds Creatio build archives.",
				[],
				false);
		}

		if (!_fileSystem.Directory.Exists(productsFolder)) {
			return new ListCreatioBuildsResult(
				"products-folder-missing",
				productsFolder,
				false,
				$"The configured 'creatio-products' folder does not exist: {productsFolder}. Fix the path in clio appsettings.json or create the folder and place build archives there.",
				[],
				false);
		}

		List<IFileInfo> zipFiles;
		try {
			zipFiles = _fileSystem.DirectoryInfo.New(productsFolder)
				.GetFiles("*.zip", System.IO.SearchOption.AllDirectories)
				.OrderByDescending(file => file.LastWriteTimeUtc)
				.ToList();
		} catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException) {
			return new ListCreatioBuildsResult(
				"products-folder-unreadable",
				productsFolder,
				true,
				$"The configured 'creatio-products' folder could not be read: {exception.Message}",
				[],
				false);
		}

		bool truncated = zipFiles.Count > MaxBuilds;
		List<ListCreatioBuildItem> builds = zipFiles
			.Take(MaxBuilds)
			.Select(file => new ListCreatioBuildItem(
				file.Name,
				file.FullName,
				file.Length,
				file.LastWriteTimeUtc.ToString("O")))
			.ToList();

		string status = builds.Count == 0 ? "no-builds-found" : "ok";
		string message = builds.Count == 0
			? $"No .zip build archives were found under {productsFolder}."
			: truncated
				? $"Found {zipFiles.Count} build archives under {productsFolder}; returning the {MaxBuilds} most recently modified."
				: $"Found {builds.Count} build archive(s) under {productsFolder}.";

		return new ListCreatioBuildsResult(status, productsFolder, true, message, builds, truncated);
	}
}

/// <summary>
/// Structured result for <c>list-creatio-builds</c>.
/// </summary>
public sealed record ListCreatioBuildsResult(
	[property: JsonPropertyName("status")]
	[property: Description("Discovery status: ok, no-builds-found, products-folder-missing, products-folder-not-configured, or products-folder-unreadable")]
	string Status,

	[property: JsonPropertyName("products-folder")]
	[property: Description("Resolved creatio-products folder configured in clio appsettings.json")]
	string ProductsFolder,

	[property: JsonPropertyName("products-folder-exists")]
	[property: Description("Whether the configured creatio-products folder exists on disk")]
	bool ProductsFolderExists,

	[property: JsonPropertyName("message")]
	[property: Description("Human-readable summary or remediation hint")]
	string Message,

	[property: JsonPropertyName("builds")]
	[property: Description("Discovered build archives, newest first")]
	IReadOnlyList<ListCreatioBuildItem> Builds,

	[property: JsonPropertyName("truncated")]
	[property: Description("True when more builds exist than were returned")]
	bool Truncated
);

/// <summary>
/// A single Creatio build archive discovered under the products folder.
/// </summary>
public sealed record ListCreatioBuildItem(
	[property: JsonPropertyName("file-name")]
	[property: Description("Build archive file name")]
	string FileName,

	[property: JsonPropertyName("full-path")]
	[property: Description("Absolute path to pass as the deploy-creatio zipFile argument")]
	string FullPath,

	[property: JsonPropertyName("size-bytes")]
	[property: Description("Archive size in bytes")]
	long SizeBytes,

	[property: JsonPropertyName("modified-on-utc")]
	[property: Description("Last write time of the archive in ISO-8601 UTC")]
	string ModifiedOnUtc
);
