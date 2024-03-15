using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Json;
using System.Text;
using CommandLine;
using Terrasoft.Common;

namespace Clio.Command.ApplicationCommand
{

	[Verb("set-app-version", Aliases = new string[] { "appversion" }, HelpText = "Set application version")]
	internal class SetApplicationVersionOption
	{
		[Option('v', "AppVersion", Required = true, HelpText = "Application version")]
		public string Version
		{
			get;
			internal set;
		}

		[Value(0, MetaName = "WorkspacePath", Required = true, HelpText = "Workspace folder path")]
		public string WorspaceFolderPath
		{
			get;
			internal set;
		}

		[Option('p', "PackageName", Required = false, HelpText = "Package name")]
		public string PackageName
		{
			get;
			internal set;
		}
	}

	internal class SetApplicationVersionCommand : Command<SetApplicationVersionOption>
	{
		private readonly IFileSystem fileSystem;

		public SetApplicationVersionCommand(IFileSystem fileSystem)
        {
			this.fileSystem = fileSystem;
		}
        public override int Execute(SetApplicationVersionOption options) {
			string packagesFolderPath = Path.Combine(options.WorspaceFolderPath, "packages");
			string[] appDescriptorPaths = fileSystem.Directory.GetFiles(packagesFolderPath, "app-descriptor.json", SearchOption.AllDirectories);
			if (appDescriptorPaths.Length > 1) {
				string code = string.Empty;
				foreach (var descriptor in appDescriptorPaths) {
					string actualCode = JsonObject.Parse(fileSystem.File.ReadAllText(descriptor))["Code"].ToString();
					if (code != actualCode && code != string.Empty) {
						StringBuilder exceptionMessage = new StringBuilder();
						exceptionMessage.AppendLine("Find more than one applications: ");
						foreach (var path in appDescriptorPaths) {
							exceptionMessage.AppendLine(path);
						}
						throw new Exception(exceptionMessage.ToString());
					} else {
						code = actualCode;
					}
				}
				if (options.PackageName.IsNullOrEmpty()) {
					StringBuilder exceptionMessage = new StringBuilder();
					exceptionMessage.AppendLine($"Find more than one descriptors for application {code}. Specify package name.");
					foreach (var path in appDescriptorPaths) {
						exceptionMessage.AppendLine(path);
					}
					throw new Exception(exceptionMessage.ToString());
				}
			}
			string appDescriptorPath = appDescriptorPaths[0];
			var objectJson = JsonObject.Parse(fileSystem.File.ReadAllText(appDescriptorPath));
			objectJson["Version"] = options.Version;
			fileSystem.File.WriteAllText(appDescriptorPath, objectJson.ToString());
			return 0;
		}
	}
}
