using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using CommandLine;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

[McpServerResourceType]
public class GetHelpResources(IFileSystem fileSystem){
	private static readonly IReadOnlyDictionary<string, string> McpToCliCommandMap =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["show-web-app-list"] = "show-web-app-list",
			["show-webApp-list"] = "show-web-app-list",
			["restart-by-environment"] = "restart-web-app",
			["restart-by-environment-name"] = "restart-web-app",
			["restart-by-environmentName"] = "restart-web-app",
			["restart-by-credentials"] = "restart-web-app",
			["clear-redis-by-environment"] = "clear-redis-db",
			["clear-redis-db-by-environment"] = "clear-redis-db",
			["clear-redis-by-credentials"] = "clear-redis-db",
			["clear-redis-db-by-credentials"] = "clear-redis-db",
			["start-creatio"] = "start",
			["stop-creatio"] = "stop",
			["stop-all-creatio"] = "stop",
			["StopAllCreatio"] = "stop"
		};
	
	[McpServerResource(UriTemplate = "docs://help/command/{commandName}", Name = "Help Article")]
	[Description("Returns a help article by CLI command name or supported MCP tool alias.")]
	public ResourceContents GetArticle(string commandName) {
		try {
			commandName = commandName.Trim();
			string resolvedCommandName = ResolveCommandName(commandName);
			List<Type> optionTypes = GetOptionTypes(resolvedCommandName);
			if (optionTypes.Count == 0) {
				return GetGenericGetError(commandName);
			}
			string helpFileContent = GetHelpFileContentByOptionType(optionTypes.First());
			if (!string.Equals(commandName, resolvedCommandName, StringComparison.OrdinalIgnoreCase)) {
				helpFileContent =
					$"MCP help mapping: `{commandName}` resolves to CLI command `{resolvedCommandName}`.{Environment.NewLine}{Environment.NewLine}{helpFileContent}";
			}
			return GetTextResourceContents(commandName, helpFileContent);
		}
		catch {
			return GetGenericGetError(commandName);
		}
	}

	private static string ResolveCommandName(string commandName) =>
		McpToCliCommandMap.TryGetValue(commandName, out string? resolvedCommandName)
			? resolvedCommandName
			: commandName;

	private string GetHelpFileContentByOptionType(Type optionType) {
		string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
		string helpDir = fileSystem.Path.Join(baseDirectory, "help", "en");
		string helpFileNameByAttributeName = optionType.GetCustomAttribute<VerbAttribute>()?.Name;
		string helpFilePath = fileSystem.Path.Join(helpDir, helpFileNameByAttributeName + ".txt");
		return fileSystem.File.ReadAllText(helpFilePath);
	}
	
	
	private static TextResourceContents GetGenericGetError(string commandName) {
		return GetTextResourceContents(commandName,$"{commandName} command does not provide documentation.");
	}

	private static List<Type> GetOptionTypes(string commandName) {
		List<Type> optionTypes = Assembly.GetExecutingAssembly().GetTypes().Where(t =>
			t.CustomAttributes.Any(attribute => attribute.AttributeType == typeof(VerbAttribute))
			&& (t.GetCustomAttribute<VerbAttribute>()?.Name == commandName || t.GetCustomAttribute<VerbAttribute>()!.Aliases.Contains(commandName))
		).ToList();
		return optionTypes;
	}
	
	private static TextResourceContents GetTextResourceContents(string commandName, string text) {
		return new TextResourceContents {
			Uri = $"docs://help/command/{commandName}",
			MimeType = "text/plain",
			Text = text
		};
	}
}
