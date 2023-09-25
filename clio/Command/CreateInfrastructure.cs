using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
			//Console.ForegroundColor = ;
			Console.WriteLine("**********************  IMPORTANT **********************");
			Console.WriteLine($"All files have been copied to:");
			Console.WriteLine($"\t{to}");
			Console.WriteLine();
			Console.ForegroundColor = ConsoleColor.DarkRed;
			Console.WriteLine("1. Make sure to review files and change values if needed");
			Console.WriteLine("2. If you have more than one cluster configured, make sure to switch to Rancher Desktop");
			
			Console.ForegroundColor = color;
			Console.WriteLine();
			Console.WriteLine("Clio will not deploy infrastructure automatically");
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
