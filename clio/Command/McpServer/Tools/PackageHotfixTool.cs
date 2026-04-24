using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class PackageHotfixTool(
	PackageHotFixCommand packageHotFixCommand,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<PackageHotFixCommandOptions>(packageHotFixCommand, logger, commandResolver) {

	[McpServerTool(Name = "unlock-for-hotfix", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Unlocks a Creatio package for hotfix editing by starting hotfix state on the remote environment.")]
	public CommandExecutionResult UnlockForHotfix(
		[Description("unlock-for-hotfix parameters")] [Required] PackageHotfixArgs args
	) {
		PackageHotFixCommandOptions options = new() {
			PackageName = args.PackageName,
			Enable = true,
			Environment = args.EnvironmentName
		};
		return InternalExecute<PackageHotFixCommand>(options);
	}

	[McpServerTool(Name = "finish-hotfix", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("Finishes hotfix state for a Creatio package, locking it back after hotfix editing.")]
	public CommandExecutionResult FinishHotfix(
		[Description("finish-hotfix parameters")] [Required] PackageHotfixArgs args
	) {
		PackageHotFixCommandOptions options = new() {
			PackageName = args.PackageName,
			Enable = false,
			Environment = args.EnvironmentName
		};
		return InternalExecute<PackageHotFixCommand>(options);
	}
}

public record PackageHotfixArgs(
	[property: JsonPropertyName("package-name")]
	[Description("Package name")]
	[Required]
	string PackageName,

	[property: JsonPropertyName("environment-name")]
	[Description("Target Creatio environment name")]
	[Required]
	string EnvironmentName
);
