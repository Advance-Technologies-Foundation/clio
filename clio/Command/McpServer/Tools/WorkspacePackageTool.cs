using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for workspace package creation.
/// </summary>
public class WorkspacePackageTool(
	AddPackageCommand addPackageCommand,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<AddPackageOptions>(addPackageCommand, logger, commandResolver) {

	/// <summary>
	/// Adds a package to the specified workspace and optionally bootstraps follow-up configuration download.
	/// </summary>
	[McpServerTool(Name = "add-package", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("""
				 Adds a package to a specified local workspace.
				 
				 The workspace path is required because the command updates local workspace state and may
				 trigger follow-up configuration download behavior. When `build-zip-path` is omitted, the
				 follow-up flow may need the requested environment to download configuration from Creatio.
				 """)]
	public CommandExecutionResult AddPackage(
		[Description("add-package parameters")] [Required] AddPackageArgs args
	) {
		AddPackageOptions options = new() {
			Name = args.Name,
			AsApp = args.AsApp ?? false,
			BuildZipPath = args.BuildZipPath,
			Environment = args.EnvironmentName,
			WorkspacePath = args.WorkspacePath
		};
		return InternalExecute<AddPackageCommand>(options);
	}
}

/// <summary>
/// MCP tool for workspace test project creation.
/// </summary>
public class CreateTestProjectTool(
	CreateTestProjectCommand createTestProjectCommand,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<CreateTestProjectOptions>(createTestProjectCommand, logger, commandResolver) {

	/// <summary>
	/// Creates a new test project for a package in the specified workspace.
	/// </summary>
	[McpServerTool(Name = "new-test-project", ReadOnly = false, Destructive = false, Idempotent = false,
		OpenWorld = false)]
	[Description("""
				 Creates a new unit test project for a package in the specified local workspace.
				 
				 The workspace path is required because the command generates files under that workspace and
				 updates the workspace solution structure.
				 """)]
	public CommandExecutionResult CreateTestProject(
		[Description("new-test-project parameters")] [Required] CreateTestProjectArgs args
	) {
		CreateTestProjectOptions options = new() {
			PackageName = args.PackageName,
			Environment = args.EnvironmentName,
			WorkspacePath = args.WorkspacePath
		};
		return InternalExecute<CreateTestProjectCommand>(options);
	}
}

/// <summary>MCP tool for creating a portable Creatio integration-test project.</summary>
public class CreateIntegrationTestProjectTool(
	CreateIntegrationTestProjectCommand command,
	ILogger logger)
	: BaseTool<CreateIntegrationTestProjectOptions>(command, logger) {

	/// <summary>Creates a scenario-neutral integration-test project in a local clio workspace.</summary>
	[McpServerTool(Name = "new-integration-test-project", ReadOnly = false, Destructive = false,
		Idempotent = false, OpenWorld = false)]
	[Description("""
		Creates a portable NUnit, ATF.Repository, FluentAssertions, and Allure integration-test project.
		Read get-guidance name=integration-testing before adding scenario-specific tests. The generated
		project accepts Creatio URL, runtime, and password or access-token authentication from NUnit
		parameters or environment variables and does not depend on a local clio environment registry.
		""")]
	public CommandExecutionResult Create(
		[Description("new-integration-test-project parameters")] [Required] CreateIntegrationTestProjectArgs args) {
		CreateIntegrationTestProjectOptions options = new() {
			PackageName = args.PackageName,
			TargetFramework = args.TargetFramework ?? "net10.0",
			WorkspacePath = args.WorkspacePath
		};
		return InternalExecute(options);
	}
}

/// <summary>
/// Arguments for the <c>add-package</c> MCP tool.
/// </summary>
public record AddPackageArgs(
	[property:JsonPropertyName("name")]
	[Description("Package name")]
	[Required]
	string Name,

	[property:JsonPropertyName("workspace-path")]
	[Description("Absolute path to the local workspace")]
	[Required]
	string WorkspacePath,

	[property:JsonPropertyName("environment-name")]
	[Description("Creatio environment name used for follow-up download when needed")]
	string EnvironmentName = null,

	[property:JsonPropertyName("as-app")]
	[Description("Whether to create an application descriptor for the package")]
	bool? AsApp = null,

	[property:JsonPropertyName("build-zip-path")]
	[Description("Path to a Creatio zip file or extracted directory to get configuration from")]
	string BuildZipPath = null
);

/// <summary>
/// Arguments for the <c>new-test-project</c> MCP tool.
/// </summary>
public record CreateTestProjectArgs(
	[property:JsonPropertyName("package-name")]
	[Description("Workspace package name")]
	[Required]
	string PackageName,

	[property:JsonPropertyName("workspace-path")]
	[Description("Absolute path to the local workspace")]
	[Required]
	string WorkspacePath,

	[property:JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[property:Required]
	string EnvironmentName
);

/// <summary>Arguments for the <c>new-integration-test-project</c> MCP tool.</summary>
public record CreateIntegrationTestProjectArgs(
	[property: JsonPropertyName("package-name"), Description("Workspace package name"), Required]
	string PackageName,
	[property: JsonPropertyName("workspace-path"), Description("Absolute path to the local workspace"), Required]
	string WorkspacePath,
	[property: JsonPropertyName("target-framework"), Description("Generated project target framework; defaults to net10.0")]
	string TargetFramework = null);
