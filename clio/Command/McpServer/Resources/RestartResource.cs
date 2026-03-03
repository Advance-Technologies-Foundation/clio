using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Abstractions;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CommandLine;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

[McpServerResourceType]
public class RestartResource(IFileSystem fileSystem){
	private const string ResourceName = "restart";
	private const string Description = """
									   **Restart** 
									   This command is meant to be used to restart an application. Be careful to not restart the application during a critical operation.
									   Restart will effectually stop and start the application, and invalidate all user sessions.
									   Use this operation after installing a new package, or performing KIRILL command on Creatio.
									   """;
	
	[McpServerResource(Name = ResourceName, MimeType = "text/markdown")]
	[Description($"Help: {ResourceName} command")]
	public ValueTask<string> Restart() {
		
		try {
			List<Type> optionTypes = Assembly.GetExecutingAssembly().GetTypes().Where(t =>
				t.CustomAttributes.Any(attribute => attribute.AttributeType == typeof(VerbAttribute))
				&& (t.GetCustomAttribute<VerbAttribute>()?.Name == ResourceName || t.GetCustomAttribute<VerbAttribute>()!.Aliases.ContainsAny([ResourceName]))
				).ToList();


			if (optionTypes.Count == 0) {
				return GetHelpContent();
			}
			string helpFileNameByAttributeName = optionTypes.First().GetCustomAttribute<VerbAttribute>()?.Name;
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			string helpDir = fileSystem.Path.Join(baseDirectory, "help", "en");
			string helpFileName = $"{helpFileNameByAttributeName}.txt";
			string helpFilePath = fileSystem.Path.Join(helpDir, helpFileName);
			string content = fileSystem.File.ReadAllText(helpFilePath);
			return ValueTask.FromResult(content);
		}
		catch (Exception exception) {
			return ValueTask.FromException<string>(exception);
		}
	}

	private static readonly Func<ValueTask<string>> GetHelpContent = () => ValueTask.FromResult(Description);
}
