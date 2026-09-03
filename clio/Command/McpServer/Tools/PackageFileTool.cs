using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Read-only MCP access to files materialized for a compiled Creatio package.</summary>
[McpServerToolType]
public sealed class PackageFileTool(
	ShowPackageFileContentCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ShowPackageFileContentOptions>(command, logger, commandResolver) {

	internal const string ListPackageFilesToolName = "list-package-files";
	internal const string GetPackageFileToolName = "get-package-file";

	/// <summary>Lists package-relative paths available in the package Files directory.</summary>
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = ListPackageFilesToolName, ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false)]
	[Description("List files materialized by Creatio for a compiled package, including the generated " +
		"<package>.csproj when file design mode is disabled. Use a returned relative path with " +
		"get-package-file. Listings traverse at most 10,000 filesystem entries. Requires cliogate " +
		"2.0.0.47 or newer.")]
	public PackageFileListResponse ListPackageFiles(
		[Description("Parameters: package-name (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] ListPackageFilesArgs args) {
		if (args is null || string.IsNullOrWhiteSpace(args.PackageName)) {
			return ListFailure("package-name is required and cannot be empty.");
		}
		ShowPackageFileContentOptions options = CreateOptions(args, filePath: null);
		return ExecuteResolved<ShowPackageFileContentCommand, PackageFileListResponse>(options,
			resolvedCommand => {
				resolvedCommand.TryListPackageFiles(options, out PackageFileListResponse response);
				return response.Success ? response : ListFailure(SensitiveErrorTextRedactor.Redact(response.Error));
			},
			ListFailure);
	}

	/// <summary>Reads one package file and the generated package project file.</summary>
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[McpServerTool(Name = GetPackageFileToolName, ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false)]
	[Description("Read one package-relative file and return its exact content together with the generated " +
		"<package>.csproj content. First call list-package-files and pass one of its relative paths. " +
		"Individual text files are limited to 10 MiB. Requires cliogate 2.0.0.47 or newer.")]
	public PackageFileContentResponse GetPackageFile(
		[Description("Parameters: package-name and file-path (required); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] GetPackageFileArgs args) {
		if (args is null || string.IsNullOrWhiteSpace(args.PackageName)) {
			return ContentFailure("package-name is required and cannot be empty.");
		}
		if (string.IsNullOrWhiteSpace(args.FilePath)) {
			return ContentFailure("file-path is required and cannot be empty.");
		}
		ShowPackageFileContentOptions options = CreateOptions(args, args.FilePath);
		return ExecuteResolved<ShowPackageFileContentCommand, PackageFileContentResponse>(options,
			resolvedCommand => {
				resolvedCommand.TryGetPackageFile(options, out PackageFileContentResponse response);
				if (!string.IsNullOrWhiteSpace(response.ProjectError)) {
					response.ProjectError = SensitiveErrorTextRedactor.Redact(response.ProjectError);
				}
				return response.Success
					? response
					: ContentFailure(SensitiveErrorTextRedactor.Redact(response.Error));
			},
			ContentFailure);
	}

	private static ShowPackageFileContentOptions CreateOptions(PackageFileArgsBase args, string filePath) => new() {
		Environment = args.EnvironmentName,
		Uri = args.Uri,
		Login = args.Login,
		Password = args.Password,
		PackageName = args.PackageName,
		FilePath = filePath
	};

	private static PackageFileListResponse ListFailure(string error) => new() {
		Success = false,
		Error = string.IsNullOrWhiteSpace(error) ? "Failed to list package files." : error
	};

	private static PackageFileContentResponse ContentFailure(string error) => new() {
		Success = false,
		Error = string.IsNullOrWhiteSpace(error) ? "Failed to read the package file." : error
	};
}

/// <summary>Shared connection and package arguments for package file tools.</summary>
public abstract record PackageFileArgsBase : ConnectionArgsBase {
	/// <summary>Gets the Creatio package name.</summary>
	[JsonPropertyName("package-name")]
	[Description("Creatio package name.")]
	[Required]
	public string PackageName { get; init; }
}

/// <summary>Arguments for <c>list-package-files</c>.</summary>
public sealed record ListPackageFilesArgs : PackageFileArgsBase;

/// <summary>Arguments for <c>get-package-file</c>.</summary>
public sealed record GetPackageFileArgs : PackageFileArgsBase {
	/// <summary>Gets the package-relative file path returned by <c>list-package-files</c>.</summary>
	[JsonPropertyName("file-path")]
	[Description("Package-relative path returned by list-package-files.")]
	[Required]
	public string FilePath { get; init; }
}
