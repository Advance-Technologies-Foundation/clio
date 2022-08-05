namespace Clio.Command
{
	using Clio.Common;
	using CommandLine;
	using System;
	using System.IO;

	#region Class: RestoreFromPackageBackupOptions

	[Verb("create-ui-project", Aliases = new string[] { "createup"}, HelpText = "Create UI project")]
	public class CreateUiProjectOptions : EnvironmentOptions
	{

		#region Properties: Public

		[Value(0, MetaName = "Name", Required = true, HelpText = "Project name")]
		public string Name {
			get; set;
		}

		[Option("vendor-prefix", Required = true,
			HelpText ="Skip rollback data")]
		public string VendorPrefix {
			get; set;
		}

		[Option("package", Required = true,
			HelpText = "Package name")]
		public string PackageName {
			get; set;
		}

		#endregion

	}

	#endregion


	#region Class: RestoreFromPackageBackupCommand

	internal class CreateUiProjectCommand
	{

		private const string packagesDirectoryName = "packages";
		private const string projectsDirectoryName = "projects";
		private const string angularFileName = "angular.json";
		private const string packageFileName = "package.json";
		private const string webpackConfigFileName = "webpack.config.js";

		public int Execute(CreateUiProjectOptions options, IFileSystem fileSystem) {
			try {
				var settings = new SettingsRepository().GetEnvironment();
				var projectsPath = Path.Combine(Environment.CurrentDirectory, projectsDirectoryName);
				var packagesPath = Path.Combine(Environment.CurrentDirectory, packagesDirectoryName);
				var baseDir = AppDomain.CurrentDomain.BaseDirectory;
				var tplPath = Path.Combine(baseDir, "tpl", "ui-project");
				if (!Directory.Exists(projectsPath)) {
					Directory.CreateDirectory(projectsPath);
				}
				if (!Directory.Exists(packagesPath)) {
					Directory.CreateDirectory(packagesPath);
				}
				var currentProjectPath = Path.Combine(projectsPath, options.Name);
				fileSystem.CopyDirectory(tplPath, currentProjectPath, false);
				// 
				string realCurrentDirectory = Environment.CurrentDirectory;
				Environment.CurrentDirectory = Path.Combine(Environment.CurrentDirectory, packagesDirectoryName);
				CreatioPackage package = CreatioPackage.CreatePackage(options.PackageName, settings.Maintainer);
				package.Create();
				Environment.CurrentDirectory = realCurrentDirectory;
				//
				var angularFilePath = Path.Combine(currentProjectPath, angularFileName);
				var packageFilePath = Path.Combine(currentProjectPath, packageFileName);
				var webpackConfigFilePath = Path.Combine(currentProjectPath, webpackConfigFileName);
				UpdateTemplateInfo(angularFilePath, options);
				UpdateTemplateInfo(packageFilePath, options);
				UpdateTemplateInfo(webpackConfigFilePath, options);
				Console.WriteLine("Done");
				return 0;
			} catch (Exception) {
				return 1;
			}
		}

		private void UpdateTemplateInfo(string path, CreateUiProjectOptions options) {
			var tplContent = File.ReadAllText(path);
			tplContent = tplContent.Replace("<%vendorPrefix%>", options.VendorPrefix);
			tplContent = tplContent.Replace("<%projectName%>", options.Name);
			tplContent = tplContent.Replace("<%distPath%>",
				$"{Path.Combine("../", "packages/", options.PackageName + "/", "Files/", "src/","js/",options.Name)}");
			File.WriteAllText(path, tplContent);
		}

	}

	#endregion

}
