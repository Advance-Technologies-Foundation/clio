using CommandLine;
using System;
using Clio.Common;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Security.Policy;
using System.Linq;

namespace Clio.Command
{

	[Verb("mklink", Aliases = new[] { "repolink" }, HelpText = "Create symbolic links.")]
	internal class MkLinkOptions
	{
		[Value(0, Default = "")]
		public string Link
		{
			get; set;
		}

		[Value(1, Default = "")]
		public string Target
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
					//CreateLink(options.Link, options.Target);
					Link(options.Link, options.Target);
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
			var environmentPackageFolders = ReadCreatioEnvironmentPackages(environmentPackagePath);
			var repositoryPackageFolders = ReadCreatioWorkspacePakages(repositoryPath);
			foreach ( var environmentPackageFolder in environmentPackageFolders) {
			//for(int i = 0; i < environmentPackageFolders.Count(); i++) {
				//DirectoryInfo environmentPackageFolder = environmentPackageFolders[i];
				var environmentPackageName = environmentPackageFolder.Name;
				Console.WriteLine($"Processing package '{environmentPackageName}'.");
				var repositoryPackageFolder = repositoryPackageFolders.FirstOrDefault(s => s.Name == environmentPackageName);
				if (repositoryPackageFolder != null) {
					Console.WriteLine($"Package '{environmentPackageName}' found in repository.");
					environmentPackageFolder.Delete(true);
					string repositoryPackageFolderPath = repositoryPackageFolder.FullName;
					string repositoryPackageFolderBranchesPath = Path.Combine(repositoryPackageFolderPath, "branches");
					if (Directory.Exists(repositoryPackageFolderBranchesPath)) {
						DirectoryInfo[] directories = new DirectoryInfo(repositoryPackageFolderBranchesPath).GetDirectories();
						if (directories.Count() == 1) {
							CreateLink(environmentPackageFolder.FullName, directories[0].FullName);
						} else {
							throw new NotSupportedException($"Command link not supported structure for package '{environmentPackageName}'. Expected structure contains one package version in folder '{repositoryPackageFolderBranchesPath}'.");
						}
					}
					CreateLink(environmentPackageFolder.FullName, repositoryPackageFolderPath);
				} else {
					Console.WriteLine($"Package '{environmentPackageName}' not found in repository.");
				}
			}
		}

		internal static void CreateLink(string link, string target) {
			Console.WriteLine($"Create link from '{link}' to '{target}'");
			Process mklinkProcess = Process.Start(
				new ProcessStartInfo("cmd", $"/c mklink /D \"{link}\" \"{target}\"") {
					CreateNoWindow = true
			});
			mklinkProcess.WaitForExit();
		}
	}
}
