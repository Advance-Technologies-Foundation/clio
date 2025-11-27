using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Policy;
using Clio.Common;
using CommandLine;
using DocumentFormat.OpenXml.Drawing;
using Path = System.IO.Path;

namespace Clio.Command
{
	[Verb("create-k8-files", Aliases = new string[] { "ck8f" }, HelpText = "Prepare K8 files for deployment")]
	public class CreateInfrastructureOptions
	{
		
	}

	[Verb("open-k8-files", Aliases = new string[] { "cfg-k8f", "cfg-k8s", "cfg-k8" }, HelpText = "Open folder K8 files for deployment")]
	public class OpenInfrastructureOptions
	{

	}

	public class OpenInfrastructureCommand : Command<OpenInfrastructureOptions>
	{
		public override int Execute(OpenInfrastructureOptions options) {
			string infrsatructureCfgFilesFolder = Path.Join(SettingsRepository.AppSettingsFolderPath, "infrastructure");
			try {
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					Process.Start("explorer.exe", infrsatructureCfgFilesFolder);
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
					Process.Start("open", infrsatructureCfgFilesFolder);
				}
				else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
					Process.Start("xdg-open", infrsatructureCfgFilesFolder);
				}
				else {
					Console.WriteLine($"Unsupported platform: {RuntimeInformation.OSDescription}");
					return 1;
				}
				return 0;
			}
			catch (Exception e) {
				Console.WriteLine($"Failed to open folder: {e.Message}");
				Console.WriteLine($"Folder path: {infrsatructureCfgFilesFolder}");
				return 1;
			}
		}
	}

	public class CreateInfrastructureCommand : Command<CreateInfrastructureOptions>
	{

		private readonly IFileSystem _fileSystem;

		public CreateInfrastructureCommand(IFileSystem fileSystem) {
			_fileSystem = fileSystem;
		}

		public override int Execute(CreateInfrastructureOptions options) {
			
			string to = Path.Join(SettingsRepository.AppSettingsFolderPath, "infrastructure");
			string location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			string from = Path.Join(location, "tpl","k8", "infrastructure");
			_fileSystem.CopyDirectory(from,to, true);

			var color = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.DarkRed;
			Console.WriteLine("****************************  IMPORTANT ****************************");
			Console.ForegroundColor = color;
			Console.WriteLine($"All files have been copied to:");
			Console.WriteLine($"\t{to}");
			Console.WriteLine();
			Console.ForegroundColor = ConsoleColor.DarkRed;
			Console.WriteLine("1. Make sure to review files and change values if needed");
			Console.WriteLine("2. If you have more than one cluster configured, make sure to switch to Rancher Desktop");
			Console.WriteLine();

			Console.ForegroundColor = color;
			Console.WriteLine("Files Include:");
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			IList<string[]> table = new List<string[]>();
			table.Add(new []{"Application","Version","Available on"});
			table.Add(new []{"-------------------------","------------------------","------------"});
			table.Add(new []{"Postgres SQL Server","latest","Port: 5432"});
			table.Add(new []{"Microsoft SQL Server 2022","latest developer edition","Port: 1434"});
			table.Add(new []{"Redis Server","latest", "Port: 6379"});
			table.Add(new []{"Email Listener","1.0.10", "Port: 1090"});
			
			Console.Write(TextUtilities.ConvertTableToString(table));
			Console.WriteLine();
			
			Console.ForegroundColor = color;
			Console.WriteLine();
			Console.ForegroundColor = ConsoleColor.DarkRed;
			Console.WriteLine("Clio will not deploy infrastructure automatically");
			Console.WriteLine();
			Console.ForegroundColor = color;
			Console.WriteLine($"To deploy new infrastructure execute from {to} folder in any terminal:");
			Console.ForegroundColor = ConsoleColor.DarkYellow;
			Console.WriteLine($"\tkubectl apply -f infrastructure");
			Console.ForegroundColor = color;
			Console.WriteLine();
			Console.WriteLine("Use Rancher Desktop to check if infrastructure is deployed correctly");
			
			return 0;
		}

	}
}
