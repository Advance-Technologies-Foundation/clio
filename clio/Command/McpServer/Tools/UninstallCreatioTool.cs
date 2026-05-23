using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class UninstallCreatioTool(UninstallCreatioCommand command, ILogger logger) : BaseTool<UninstallCreatioCommandOptions>(command, logger) {
	
		[Description("""
				 uninstall-creatio command completely removes local Creatio instance from
				 the machine, including the IIS site and application pool, database (both
				 local and containerized), application files, and application pool user
				 profile data.
				 
				 The command reads the database connection string from ConnectionStrings.config
				 in the Creatio installation directory and uses it to connect and drop the
				 database. This works for both local databases (PostgreSQL, MSSQL) and
				 containerized databases (Kubernetes/Rancher).
				 """)]
	public CommandExecutionResult UninstallCreatio(
		[Description("Uninstall parameters")] [Required] UninstallCreatioRunArgs args
	) {
		
		UninstallCreatioCommandOptions options = new() {
			EnvironmentName = args.EnvironmentName
		};
		return InternalExecute(options);
	}
}

public record UninstallCreatioRunArgs(
	[property:JsonPropertyName("environment-name")][Description("Creatio environment name to uninstall")] [Required] string EnvironmentName
) : ClioRunArgs;
