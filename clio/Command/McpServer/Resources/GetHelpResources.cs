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
	
	[McpServerResource(UriTemplate = "docs://help/command/{commandName}", Name = "Help Article")]
	[Description("Returns a help article by command name")]
	public ResourceContents GetArticle(string commandName) {
		try {
			List<Type> optionTypes = GetOptionTypes(commandName);
			if (optionTypes.Count == 0) {
				return GetGenericGetError(commandName);
			}
			string helpFileContent = GetHelpFileContentByOptionType(optionTypes.First());
			return GetTextResourceContents(commandName, helpFileContent);
		}
		catch {
			return GetGenericGetError(commandName);
		}
	}

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
			&& (t.GetCustomAttribute<VerbAttribute>()?.Name == commandName || t.GetCustomAttribute<VerbAttribute>()!.Aliases.ContainsAny([commandName]))
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
