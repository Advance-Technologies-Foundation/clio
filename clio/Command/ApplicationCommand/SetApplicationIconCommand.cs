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

	[Verb("set-app-icon", Aliases = new string[] { "appicon", "ai" }, HelpText = "Set application icon")]
	internal class SetApplicationIconOption
	{
		[Option('i', "app-icon", Required = true, HelpText = "Application icon path")]
		public string IconPath
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

		[Option('p', "app-name", Required = false, HelpText = "App name")]
		public string AppName
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

	internal class SetApplicationIconCommand : Command<SetApplicationIconOption>
	{
		private readonly IComposableApplicationManager _composableApplicationManager;

		public SetApplicationIconCommand(IComposableApplicationManager composableApplicationManager) {
			_composableApplicationManager = composableApplicationManager;
		}
		public override int Execute(SetApplicationIconOption options) {
			string packagesFolderPath = options.PackageFolderPath.IsNotNullOrEmpty() ?
						options.PackageFolderPath : Path.Combine(options.WorspaceFolderPath, "packages");
			_composableApplicationManager.SetIcon(packagesFolderPath, options.IconPath, options.AppName);
			return 0;
		}
	}
}
