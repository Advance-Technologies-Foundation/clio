using System.IO;
using System.IO.Abstractions;
using System.Json;
using CommandLine;

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
			string appDescriptorPath = appDescriptorPaths[0];
			var objectJson = JsonObject.Parse(fileSystem.File.ReadAllText(appDescriptorPath));
			objectJson["Version"] = options.Version;
			fileSystem.File.WriteAllText(appDescriptorPath, objectJson.ToString());
			return 0;
		}
	}
}
