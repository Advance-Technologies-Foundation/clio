using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Prompts;

/// <summary>
/// Prompt helpers for the <c>reg-web-app</c> MCP tool.
/// </summary>
[McpServerPromptType, Description("Prompt to register or update a web application in local clio settings")]
public static class RegWebAppPrompt {

	/// <summary>
	/// Builds a prompt that directs the agent to use the <c>reg-web-app</c> tool.
	/// </summary>
	/// <param name="environmentName">Environment name to create or update.</param>
	/// <param name="uri">Creatio application URI.</param>
	/// <param name="login">Login to store in local settings.</param>
	/// <param name="password">Password to store in local settings.</param>
	/// <param name="checkLogin">Whether to validate the credentials after registration.</param>
	/// <returns>Prompt text for the MCP agent.</returns>
	[McpServerPrompt(Name = "reg-web-app"), Description("Prompt to register or update a local clio web application")]
	public static string Prompt(
		[Required]
		[Description("Environment name to register or update")]
		string environmentName,
		[Description("Creatio application URI")]
		string uri = null,
		[Description("User login")]
		string login = null,
		[Description("User password")]
		string password = null,
		[Description("Try login after registration")]
		bool checkLogin = false) =>
		$"""
		 Use clio mcp server `reg-web-app` tool to register or update local clio settings
		 for environment `{environmentName}`.
		 Include URI `{uri ?? "<not provided>"}`, login `{login ?? "<not provided>"}`, and
		 password `{(string.IsNullOrWhiteSpace(password) ? "<not provided>" : "<provided>")}`.
		 Set `check-login` to `{checkLogin}`. When a URI is provided, clio automatically
		 detects whether the site uses .NET Core / NET8 or .NET Framework — no runtime hint is needed.
		 If you need to inspect registered environments first,
		 use `ShowWebAppList`. If you need command syntax details, use `docs://help/command/reg-web-app`.
		 """;
}
