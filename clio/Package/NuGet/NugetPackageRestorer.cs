using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common;

namespace Clio.Project.NuGet
{

	#region Class: NugetPackageRestorer

	public class NugetPackageRestorer : INugetPackageRestorer
	{

		#region Fields: Private

		private const string NugetRestoreProjName = "NugetRestoreProj.csproj";
		private readonly IDotnetExecutor _dotnetExecutor;
		private readonly ITemplateProvider _templateProvider;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;
		private readonly IPackageArchiver _packageArchiver;
		private readonly ILogger _logger;
		private readonly INugetPackagesProvider _nugetPackagesProvider;

		#endregion

		#region Constructors: Public

		public NugetPackageRestorer(INugetPackagesProvider nugetPackagesProvider, IPackageArchiver packageArchiver,
				ITemplateProvider templateProvider, IDotnetExecutor dotnetExecutor,
				IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem, ILogger logger) {
			nugetPackagesProvider.CheckArgumentNull(nameof(nugetPackagesProvider));
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			dotnetExecutor.CheckArgumentNull(nameof(dotnetExecutor));
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));
			_nugetPackagesProvider = nugetPackagesProvider;
			_packageArchiver = packageArchiver;
			_dotnetExecutor = dotnetExecutor;
			_templateProvider = templateProvider;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		#endregion

		#region Methods: Private

		private static void CheckArguments(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl) {
			nugetPackageFullName.CheckArgumentNull(nameof(nugetPackageFullName));
			nugetSourceUrl.CheckArgumentNullOrWhiteSpace(nameof(nugetSourceUrl));
		}

		private string ReplaceMacro(string template, string name, string version) {
			return template.Replace("$name$", name)
				.Replace("$version$", version);
		}

		private void CreateNugetRestoreProj(string nugetPackProjPath, string name, string version) {
			string template = _templateProvider.GetTemplate(NugetRestoreProjName);
			string nugetRestoreProjFileContent = ReplaceMacro(template, name, version);
			File.WriteAllText(nugetPackProjPath, nugetRestoreProjFileContent);
		}

		private string GetLastVersionPackage(string name, string nugetSourceUrl) {
			LastVersionNugetPackages lastVersionPackage =
				_nugetPackagesProvider.GetLastVersionPackages(name, nugetSourceUrl);
			return lastVersionPackage?.Last.Version.ToString();
		}

		private string RestorePackage(string nugetRestoreProjPath, string nugetSourceUrl,
				string destinationNupkgDirectory) {
			string packCommand = $"restore \"{nugetRestoreProjPath}\" --source {nugetSourceUrl} " +
				$"--packages \"{destinationNupkgDirectory}\" --force --no-cache";
			return _dotnetExecutor.Execute(packCommand, true);
		}

		private string Restore(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl, string destinationDirectory) {
			destinationDirectory = _fileSystem.GetCurrentDirectoryIfEmpty(destinationDirectory);
			return _workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				string version = nugetPackageFullName.Version == PackageVersion.LastVersion
					? GetLastVersionPackage(nugetPackageFullName.Name, nugetSourceUrl)
					: nugetPackageFullName.Version;
				string nugetRestoreProjPath = Path.Combine(tempDirectory, NugetRestoreProjName);
				CreateNugetRestoreProj(nugetRestoreProjPath, nugetPackageFullName.Name, version);
				string result = RestorePackage(nugetRestoreProjPath, nugetSourceUrl, destinationDirectory)
					.Replace(nugetRestoreProjPath, $"\"{destinationDirectory}\"");
				return result;
			});
		}

		private void RestoreToDirectory(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
			string destinationNupkgDirectory, Action<IEnumerable<string>> onRestored) {
			string result = string.Empty;
			_workingDirectoriesProvider.CreateTempDirectory(restoreTempDirectory =>{
				result = Restore(nugetPackageFullName, nugetSourceUrl, restoreTempDirectory)
					.Replace(restoreTempDirectory, $"{destinationNupkgDirectory}");
				_logger.WriteLine(result);
				IEnumerable<string> gzipPackedPackagesFiles =
					_packageArchiver.FindGzipPackedPackagesFiles(restoreTempDirectory);
				onRestored(gzipPackedPackagesFiles);
			});
		}

		#endregion

		#region Methods: Public

		public void RestoreToNugetFileStorage(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
				string destinationNupkgDirectory) {
			CheckArguments(nugetPackageFullName, nugetSourceUrl);
			string result = Restore(nugetPackageFullName, nugetSourceUrl, destinationNupkgDirectory);
			_logger.WriteLine(result);
		}

		public void RestoreToDirectory(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
				string destinationNupkgDirectory, bool overwrite) {
			CheckArguments(nugetPackageFullName, nugetSourceUrl);
			RestoreToDirectory(nugetPackageFullName, nugetSourceUrl, destinationNupkgDirectory,
				gzipPackedPackagesFiles => {
					_fileSystem.CopyFiles(gzipPackedPackagesFiles, destinationNupkgDirectory, overwrite);
				});
		}

		public void RestoreToPackageStorage(NugetPackageFullName nugetPackageFullName, string nugetSourceUrl,
				string destinationNupkgDirectory, bool overwrite) {
			CheckArguments(nugetPackageFullName, nugetSourceUrl);
			RestoreToDirectory(nugetPackageFullName, nugetSourceUrl, destinationNupkgDirectory,
				gzipPackedPackagesFiles => {
					_packageArchiver.Unpack(gzipPackedPackagesFiles, overwrite, false, destinationNupkgDirectory);
				});
		}

		#endregion

	}

	#endregion

}