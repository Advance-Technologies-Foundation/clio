using CommandLine;
using System;
using Clio.Common;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Clio.Command
{

	[Verb("attachPackageStore", Aliases = new[] { "aps", "mklink" }, HelpText = "Attach package store repository to environment.")]
	internal class MkLinkOptions
	{
		[Option("repoPath", Required = true,
			HelpText = "Path to package repository folder", Default = null)]
		public string RepoPath
		{
			get; set;
		}

		[Option("envPkgPath", Required = true,
			HelpText = "Path to environment package folder ({LOCAL_CREATIO_PATH}Terrasoft.WebApp\\Terrasoft.Configuration\\Pkg)", Default = null)]
		public string envPkgPath
		{
			get; set;
		}

	}

	class MkLinkCommand : Command<MkLinkOptions>
	{

		public MkLinkCommand() {
		}

		public override int Execute(MkLinkOptions options) {
			try {
				if (OperationSystem.Current.IsWindows) {
					Link(options.envPkgPath, options.RepoPath);
					Console.WriteLine("Done.");
					return 0;
				}
				Console.WriteLine("Clio mklink command is only supported on: 'windows'.");
				return 1;
			} catch (Exception e) {
				Console.WriteLine(e);
				return 1;
			}
		}

		public static IEnumerable<DirectoryInfo> ReadCreatioEnvironmentPackages(string pkgPath) {
			return new DirectoryInfo(pkgPath).GetDirectories();
		}

		public static IEnumerable<DirectoryInfo> ReadCreatioWorkspacePakages(string repositoryPath) {
			var workspacePackagesPath = Path.Combine(repositoryPath, "packages");
			if (Directory.Exists(workspacePackagesPath)) {
				return ReadCreatioEnvironmentPackages(workspacePackagesPath);
			}
			return ReadCreatioEnvironmentPackages(repositoryPath);
		}


		internal static void Link(string environmentPackagePath, string repositoryPath) {
			var environmentPackageFolders = ReadCreatioEnvironmentPackages(environmentPackagePath).ToList();
			var repositoryPackageFolders = ReadCreatioWorkspacePakages(repositoryPath);
			for(int i = 0; i < environmentPackageFolders.Count(); i++) {
				DirectoryInfo environmentPackageFolder = environmentPackageFolders[i];
				var environmentPackageName = environmentPackageFolder.Name;
				Console.WriteLine($"Processing package '{environmentPackageName}' {i+1} of {environmentPackageFolders.Count()}.");
				var repositoryPackageFolder = repositoryPackageFolders.FirstOrDefault(s => s.Name == environmentPackageName);
				if (repositoryPackageFolder != null) {
					Console.WriteLine($"Package '{environmentPackageName}' found in repository.");
					environmentPackageFolder.Delete(true);
					string repositoryPackageFolderPath = repositoryPackageFolder.FullName;
					string packageContentFolderPath = PackageUtilities.GetPackageContentFolderPath(repositoryPackageFolderPath);
					CreateLink(packageContentFolderPath, repositoryPackageFolderPath);
				} else {
					Console.WriteLine($"Package '{environmentPackageName}' not found in repository.");
				}
			}
		}

		internal static void CreateLink(string link, string target) {
			Process mklinkProcess = Process.Start(
				new ProcessStartInfo("cmd", $"/c mklink /D \"{link}\" \"{target}\"") {
					CreateNoWindow = true
			});
			mklinkProcess.WaitForExit();
		}
	}
}
