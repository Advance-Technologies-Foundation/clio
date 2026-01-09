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
		[Option('p', "path", Required = false,
			HelpText = "Path to infrastructure files (default: auto-detected from clio settings)")]
		public string InfrastructurePath { get; set; }
		
		[Option("pg-limit-memory", Required = false, Default = "4Gi",
			HelpText = "PostgreSQL memory limit (default: 4Gi)")]
		public string PostgresLimitMemory { get; set; }
		
		[Option("pg-limit-cpu", Required = false, Default = "2",
			HelpText = "PostgreSQL CPU limit (default: 2)")]
		public string PostgresLimitCpu { get; set; }
		
		[Option("pg-request-memory", Required = false, Default = "2Gi",
			HelpText = "PostgreSQL memory request (default: 2Gi)")]
		public string PostgresRequestMemory { get; set; }
		
		[Option("pg-request-cpu", Required = false, Default = "1",
			HelpText = "PostgreSQL CPU request (default: 1)")]
		public string PostgresRequestCpu { get; set; }
		
		[Option("mssql-limit-memory", Required = false, Default = "4Gi",
			HelpText = "MSSQL memory limit (default: 4Gi)")]
		public string MssqlLimitMemory { get; set; }
		
		[Option("mssql-limit-cpu", Required = false, Default = "2",
			HelpText = "MSSQL CPU limit (default: 2)")]
		public string MssqlLimitCpu { get; set; }
		
		[Option("mssql-request-memory", Required = false, Default = "2Gi",
			HelpText = "MSSQL memory request (default: 2Gi)")]
		public string MssqlRequestMemory { get; set; }
		
		[Option("mssql-request-cpu", Required = false, Default = "1",
			HelpText = "MSSQL CPU request (default: 1)")]
		public string MssqlRequestCpu { get; set; }
	}


	[Verb("open-k8-files", Aliases = new string[] { "cfg-k8f", "cfg-k8s", "cfg-k8" }, HelpText = "Open folder K8 files for deployment")]
	public class OpenInfrastructureOptions
	{

	}

	public class OpenInfrastructureCommand : Command<OpenInfrastructureOptions>
	{
		private readonly IInfrastructurePathProvider _infrastructurePathProvider;
		
		public OpenInfrastructureCommand(IInfrastructurePathProvider infrastructurePathProvider = null) {
			_infrastructurePathProvider = infrastructurePathProvider ?? new InfrastructurePathProvider();
		}
		
		public override int Execute(OpenInfrastructureOptions options) {
			string infrsatructureCfgFilesFolder = _infrastructurePathProvider.GetInfrastructurePath();
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
		private readonly IInfrastructurePathProvider _infrastructurePathProvider;

		public CreateInfrastructureCommand(IFileSystem fileSystem, IInfrastructurePathProvider infrastructurePathProvider = null) {
			_fileSystem = fileSystem;
			_infrastructurePathProvider = infrastructurePathProvider ?? new InfrastructurePathProvider();
		}

		public override int Execute(CreateInfrastructureOptions options) {
			string to = _infrastructurePathProvider.GetInfrastructurePath(options.InfrastructurePath);
			string location = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			string from = Path.Join(location, "tpl","k8", "infrastructure");
			_fileSystem.CopyDirectory(from,to, true);
			
			// Process template files with variable substitution
			var replacements = new Dictionary<string, string>
			{
				{ "{{PG_LIMIT_MEMORY}}", options.PostgresLimitMemory },
				{ "{{PG_LIMIT_CPU}}", options.PostgresLimitCpu },
				{ "{{PG_REQUEST_MEMORY}}", options.PostgresRequestMemory },
				{ "{{PG_REQUEST_CPU}}", options.PostgresRequestCpu },
				{ "{{MSSQL_LIMIT_MEMORY}}", options.MssqlLimitMemory },
				{ "{{MSSQL_LIMIT_CPU}}", options.MssqlLimitCpu },
				{ "{{MSSQL_REQUEST_MEMORY}}", options.MssqlRequestMemory },
				{ "{{MSSQL_REQUEST_CPU}}", options.MssqlRequestCpu }
			};
			
			// Process PostgreSQL StatefulSet
			string postgresStatefulSetPath = Path.Join(to, "postgres", "postgres-stateful-set.yaml");
			if (_fileSystem.ExistsFile(postgresStatefulSetPath))
			{
				string content = _fileSystem.ReadAllText(postgresStatefulSetPath);
				foreach (var kvp in replacements)
				{
					content = content.Replace(kvp.Key, kvp.Value);
				}
				_fileSystem.WriteAllTextToFile(postgresStatefulSetPath, content);
			}
			
			// Process MSSQL StatefulSet
			string mssqlStatefulSetPath = Path.Join(to, "mssql", "mssql-stateful-set.yaml");
			if (_fileSystem.ExistsFile(mssqlStatefulSetPath))
			{
				string content = _fileSystem.ReadAllText(mssqlStatefulSetPath);
				foreach (var kvp in replacements)
				{
					content = content.Replace(kvp.Key, kvp.Value);
				}
				_fileSystem.WriteAllTextToFile(mssqlStatefulSetPath, content);
			}

			var color = Console.ForegroundColor;
		
			// Display resource configuration
			Console.WriteLine();
			Console.ForegroundColor = ConsoleColor.Cyan;
			Console.WriteLine("Resource Configuration:");
			Console.WriteLine($"  PostgreSQL: Memory Limit={options.PostgresLimitMemory}, CPU Limit={options.PostgresLimitCpu}");
			Console.WriteLine($"              Memory Request={options.PostgresRequestMemory}, CPU Request={options.PostgresRequestCpu}");
			Console.WriteLine($"  MSSQL:      Memory Limit={options.MssqlLimitMemory}, CPU Limit={options.MssqlLimitCpu}");
			Console.WriteLine($"              Memory Request={options.MssqlRequestMemory}, CPU Request={options.MssqlRequestCpu}");
			Console.ForegroundColor = color;
			Console.WriteLine();
		
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