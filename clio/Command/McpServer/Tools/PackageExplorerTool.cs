using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Read-only, environment-aware tools for Creatio dependency contract v1.</summary>
[McpServerToolType]
public sealed class PackageExplorerTool(PackageExplorerCommand command, ILogger logger, IToolCommandResolver resolver)
	: BaseTool<PackageExplorerOptions>(command, logger, resolver) {
	/// <summary>Stable get-pkg-dependencies tool name.</summary>
	internal const string GetDependenciesToolName = "get-pkg-dependencies";

	/// <summary>Read direct or transitive dependencies; use before editing schemas or diagnosing a missing package dependency.</summary>
	[McpServerTool(Name = GetDependenciesToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Read direct or transitive dependencies; use before editing schemas or diagnosing a missing package dependency. Requires advertised Creatio dependency contract v1; older servers are refused. Read get-guidance name=package-dependencies before interpreting a verdict.")]
	public CommandExecutionResult GetDependencies([Required] GetDependenciesExplorerArgs args) =>
		InternalExecute<PackageExplorerCommand>(new GetPackageDependenciesOptions {
			Environment = args.EnvironmentName,
			Package = args.Package,
			Dependants = args.Dependants,
			Transitive = args.Transitive
		});
	/// <summary>Stable pkg-dependency-path tool name.</summary>
	internal const string GetPathToolName = "pkg-dependency-path";

	/// <summary>Explain a shortest directed path between two packages; cycles are handled safely.</summary>
	[McpServerTool(Name = GetPathToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Explain a shortest directed path between two packages; cycles are handled safely. Requires advertised Creatio dependency contract v1; older servers are refused. Read get-guidance name=package-dependencies before interpreting a verdict.")]
	public CommandExecutionResult GetPath([Required] GetPathExplorerArgs args) =>
		InternalExecute<PackageExplorerCommand>(new PackageDependencyPathOptions {
			Environment = args.EnvironmentName,
			From = args.From,
			To = args.To
		});
	/// <summary>Stable pkg-dependency-why tool name.</summary>
	internal const string GetReasonsToolName = "pkg-dependency-why";

	/// <summary>Explain registered metadata references. No known reasons does not prove unused code. Removal checking performs no write.</summary>
	[McpServerTool(Name = GetReasonsToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Explain registered metadata references. No known reasons does not prove unused code. Removal checking performs no write. Requires advertised Creatio dependency contract v1; older servers are refused. Read get-guidance name=package-dependencies before interpreting a verdict.")]
	public CommandExecutionResult GetReasons([Required] GetReasonsExplorerArgs args) =>
		InternalExecute<PackageExplorerCommand>(new PackageDependencyWhyOptions {
			Environment = args.EnvironmentName,
			From = args.From,
			To = args.To,
			Details = args.Details,
			CheckRemoval = args.CheckRemoval
		});
	/// <summary>Stable find-pkg-by-schema tool name.</summary>
	internal const string FindSchemaToolName = "find-pkg-by-schema";

	/// <summary>Find exact schema owners, or resolve entity visibility using package-name and purpose. Use when a schema cannot be opened or a dependency appears missing. hasMore means incomplete results.</summary>
	[McpServerTool(Name = FindSchemaToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Find exact schema owners, or resolve entity visibility using package-name and purpose. Use when a schema cannot be opened or a dependency appears missing. hasMore means incomplete results. Requires advertised Creatio dependency contract v1; older servers are refused. Read get-guidance name=package-dependencies before interpreting a verdict.")]
	public CommandExecutionResult FindSchema([Required] FindSchemaExplorerArgs args) =>
		InternalExecute<PackageExplorerCommand>(new FindPackageBySchemaOptions {
			Environment = args.EnvironmentName,
			Schema = args.Schema,
			ManagerName = args.ManagerName,
			Package = args.Package,
			Purpose = args.Purpose,
			Contains = args.Contains,
			Limit = args.Limit
		});
	/// <summary>Stable export-pkg-graph tool name.</summary>
	internal const string ExportGraphToolName = "export-pkg-graph";

	/// <summary>Export package graph as json or dot. Revision describes topology, not a schema snapshot.</summary>
	[McpServerTool(Name = ExportGraphToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Export package graph as json or dot. Revision describes topology, not a schema snapshot. Requires advertised Creatio dependency contract v1; older servers are refused. Read get-guidance name=package-dependencies before interpreting a verdict.")]
	public CommandExecutionResult ExportGraph([Required] ExportGraphExplorerArgs args) =>
		InternalExecute<PackageExplorerCommand>(new ExportPackageGraphOptions {
			Environment = args.EnvironmentName,
			Format = args.Format
		});
	/// <summary>Stable check-pkg-dependency tool name.</summary>
	internal const string CheckDependencyToolName = "check-pkg-dependency";

	/// <summary>Preview add or remove dependency rules without a write. noKnownBlockers is limited to checkedKinds, never a guarantee for dynamic code or write authorization.</summary>
	[McpServerTool(Name = CheckDependencyToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(Location = McpToolExecutionLocation.Worker, Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None, BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None, SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Preview add or remove dependency rules without a write. noKnownBlockers is limited to checkedKinds, never a guarantee for dynamic code or write authorization. Requires advertised Creatio dependency contract v1; older servers are refused. Read get-guidance name=package-dependencies before interpreting a verdict.")]
	public CommandExecutionResult CheckDependency([Required] CheckDependencyExplorerArgs args) =>
		InternalExecute<PackageExplorerCommand>(new CheckPackageDependencyOptions {
			Environment = args.EnvironmentName,
			From = args.From,
			To = args.To,
			Action = args.Action
		});
}

/// <summary>Arguments for get-pkg-dependencies.</summary>
public sealed record GetDependenciesExplorerArgs(
	[property: JsonPropertyName("environment-name"), Description("Registered Creatio environment"), Required]
	string EnvironmentName,
	[property: JsonPropertyName("package"), Description("Package name or UId"), Required]
	string Package,
	[property: JsonPropertyName("dependants"), Description("Traverse dependants")]
	bool Dependants = false,
	[property: JsonPropertyName("transitive"), Description("Include indirect dependencies")]
	bool Transitive = false
);

/// <summary>Arguments for pkg-dependency-path.</summary>
public sealed record GetPathExplorerArgs(
	[property: JsonPropertyName("environment-name"), Description("Registered Creatio environment"), Required]
	string EnvironmentName,
	[property: JsonPropertyName("from"), Description("Depending package name or UId"), Required]
	string From,
	[property: JsonPropertyName("to"), Description("Dependency package name or UId"), Required]
	string To
);

/// <summary>Arguments for pkg-dependency-why.</summary>
public sealed record GetReasonsExplorerArgs(
	[property: JsonPropertyName("environment-name"), Description("Registered Creatio environment"), Required]
	string EnvironmentName,
	[property: JsonPropertyName("from"), Description("Depending package name or UId"), Required]
	string From,
	[property: JsonPropertyName("to"), Description("Dependency package name or UId"), Required]
	string To,
	[property: JsonPropertyName("details"), Description("Return individual evidence instead of counts")]
	bool Details = false,
	[property: JsonPropertyName("check-removal"), Description("Assess removal of this direct edge")]
	bool CheckRemoval = false
);

/// <summary>Arguments for find-pkg-by-schema.</summary>
public sealed record FindSchemaExplorerArgs(
	[property: JsonPropertyName("environment-name"), Description("Registered Creatio environment"), Required]
	string EnvironmentName,
	[property: JsonPropertyName("schema"), Description("Literal schema name"), Required]
	string Schema,
	[property: JsonPropertyName("manager-name"), Description("Optional schema manager")]
	string ManagerName = null,
	[property: JsonPropertyName("package-name"), Description("Optional package context")]
	string Package = null,
	[property: JsonPropertyName("purpose"), Description("reference or extend for entity context")]
	string Purpose = "reference",
	[property: JsonPropertyName("contains"), Description("Literal contains search without context")]
	bool Contains = false,
	[property: JsonPropertyName("limit"), Description("Maximum 1-200 search rows")]
	int Limit = 200
);

/// <summary>Arguments for export-pkg-graph.</summary>
public sealed record ExportGraphExplorerArgs(
	[property: JsonPropertyName("environment-name"), Description("Registered Creatio environment"), Required]
	string EnvironmentName,
	[property: JsonPropertyName("format"), Description("json or dot")]
	string Format = "json"
);

/// <summary>Arguments for check-pkg-dependency.</summary>
public sealed record CheckDependencyExplorerArgs(
	[property: JsonPropertyName("environment-name"), Description("Registered Creatio environment"), Required]
	string EnvironmentName,
	[property: JsonPropertyName("from"), Description("Depending package name or UId"), Required]
	string From,
	[property: JsonPropertyName("to"), Description("Dependency package name or UId"), Required]
	string To,
	[property: JsonPropertyName("action"), Description("add or remove")]
	string Action = "add"
);
