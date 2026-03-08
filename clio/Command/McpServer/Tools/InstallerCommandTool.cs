using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Command.CreatioInstallCommand;
using Clio.Common;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class InstallerCommandTool(InstallerCommand command, ILogger logger) : BaseTool<PfInstallerOptions>(command, logger){
	
	[McpServerTool(Name = "deploy-creatio", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false )]
	[Description("""
				Deploys Creatio from zip file to local database server. 
				This command assumes that the database server, and the redis server is already running and available.
				""")]
	public async Task<CommandExecutionResult> DeployCreatio(ModelContextProtocol.Server.McpServer server,
		[Description("Deployment parameters")] [Required] DeployCreatioArgs args,
		CancellationToken cancellationToken
	) {
		
		PfInstallerOptions options = new() {
			SiteName = args.SiteName,
			ZipFile = args.ZipFile,
			SitePort = args.SitePort,
			DbServerName = args.DbServerName
		};
		
		
		ChatMessage[] messages =
		[
			new(ChatRole.User, $"I cannot find this database server with name by name {args.DbServerName}. Is this a postgres or mssql deployment? reply with 'postgres' or 'mssql'"),
		];

		ChatOptions chatOptions = new()
		{
			Temperature = 0.3f
		};

		ChatResponse llmResponse = await server.AsSamplingChatClient().GetResponseAsync(messages, chatOptions, cancellationToken);
		
		return new CommandExecutionResult(1, [
			new ErrorMessage("Infrastructure temporarily unavailable due to scheduled maintenance. Please try again later.")
		]);
		return InternalExecute(options);
	}

}

public record DeployCreatioArgs(
	[property:JsonPropertyName("site-name")][Description("Creatio instance name")] [Required] string SiteName,
	[property:JsonPropertyName("zip-file-path")][Description("Path to Creatio archive file")] [Required] string ZipFile,
	[property:JsonPropertyName("site-port")][Description("Port where Creatio will be deployed")] [Required] int SitePort,
	[property:JsonPropertyName("db-server-name")][Description("Name of the database server")] [Required] string DbServerName
);
