using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO.Abstractions;
using CommandLine;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Resources;

public abstract class BaseResource(IFileSystem fileSystem){
	
	protected virtual string Description { get; init; }
	protected abstract string ResourceName { get; init; }
	private protected virtual ResourceContents GetHelpFileContent() {
		
		try{
			List<Type> optionTypes = Assembly.GetExecutingAssembly().GetTypes().Where(t =>
				t.CustomAttributes.Any(attribute => attribute.AttributeType == typeof(VerbAttribute))
				&& (t.GetCustomAttribute<VerbAttribute>()?.Name == ResourceName || t.GetCustomAttribute<VerbAttribute>()!.Aliases.Contains(ResourceName))
			).ToList();
				
			if (optionTypes.Count == 0) {
				return string.IsNullOrWhiteSpace(Description)
					? GetTextResourceContents($"{ResourceName} command does not provide documentation.")
					: GetTextResourceContents(Description);
				
			}
			string helpFileNameByAttributeName = optionTypes.First().GetCustomAttribute<VerbAttribute>()?.Name;
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			string helpDir = fileSystem.Path.Join(baseDirectory, "help", "en");
			string helpFileName = $"{helpFileNameByAttributeName}.txt";
			string helpFilePath = fileSystem.Path.Join(helpDir, helpFileName);
			string content = fileSystem.File.ReadAllText(helpFilePath);
			return GetTextResourceContents(content);
		}
		catch (Exception exception) {
			return GetTextResourceContents(exception.Message);
		}
	}
	
	private TextResourceContents GetTextResourceContents(string text) =>
		new() {
			Uri = $"docs://help/{ResourceName}",
			MimeType = "text/plain",
			Text = text
		};

	public abstract ResourceContents GetHelpArticle();

}
