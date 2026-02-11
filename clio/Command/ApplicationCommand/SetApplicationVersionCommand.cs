using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Json;
using System.Text;
using Clio.ComposableApplication;
using CommandLine;
using Terrasoft.Common;

namespace Clio.Command.ApplicationCommand
{

	[Verb("set-app-version", Aliases = ["appversion"], HelpText = "Set application version")]
	internal class SetApplicationVersionOption
	{
		[Option('v', "app-version", Required = true, HelpText = "Application version")]
		public string Version
		{
			get;
			internal set;
		}

		[Value(0, MetaName = "workspace", Required = false, HelpText = "Workspace folder path")]
		public string WorspaceFolderPath
		{
			get;
			internal set;
		}

		[Option('p', "package-name", Required = false, HelpText = "Package name")]
		public string PackageName
		{
			get;
			internal set;
		}

		[Option('f', "package-folder", Required = false, HelpText = "Package folder path")]
		public string PackageFolderPath
		{
			get;
			internal set;
		}
	}

	internal class SetApplicationVersionCommand : Command<SetApplicationVersionOption>
	{
		private readonly IComposableApplicationManager _composableApplicationManager;

		public SetApplicationVersionCommand(IComposableApplicationManager composableApplicationManager)
        {
			_composableApplicationManager = composableApplicationManager;
		}
        public override int Execute(SetApplicationVersionOption options) {
			string packagesFolderPath = options.PackageFolderPath.IsNotNullOrEmpty() ?
						options.PackageFolderPath : Path.Combine(options.WorspaceFolderPath, "packages");
			_composableApplicationManager.SetVersion(packagesFolderPath, options.Version, options.PackageName);
			return 0;
		}
	}
}
