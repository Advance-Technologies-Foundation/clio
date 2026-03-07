using System.ComponentModel;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public class ShowWebAppListTool(ShowAppListCommand command, ILogger logger) : BaseTool<AppListOptions>(command, logger) {

	[McpServerTool(Name = "ShowWebAppList"), Description("Show the list of web applications (Creatrio environments) and their settings")]
	public CommandExecutionResult ShowWebAppList() {
		AppListOptions options = new() {
			Format = "raw"
		};
		return InternalExecute(options);
	}
}
