using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common;

namespace Clio.Project.NuGet
{
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

		#endregion

		#region Constructors: Public

		public NugetPackageRestorer(IPackageArchiver packageArchiver, ITemplateProvider templateProvider, IDotnetExecutor dotnetExecutor, 
				IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem, ILogger logger) {
			packageArchiver.CheckArgumentNull(nameof(packageArchiver));
			dotnetExecutor.CheckArgumentNull(nameof(dotnetExecutor));
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));
			_packageArchiver = packageArchiver;
			_dotnetExecutor = dotnetExecutor;
			_templateProvider = templateProvider;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		#endregion

		#region Methods: Private

		private static void CheckArguments(string packageName, string version, string nugetSourceUrl) {
			packageName.CheckArgumentNullOrWhiteSpace(nameof(packageName));
			version.CheckArgumentNullOrWhiteSpace(nameof(version));
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

		private string RestorePackage(string nugetRestoreProjPath, string nugetSourceUrl, 
				string destinationNupkgDirectory) {
			string packCommand = $"restore \"{nugetRestoreProjPath}\" --source {nugetSourceUrl} " + 
				$"--packages \"{destinationNupkgDirectory}\" --force";
			return _dotnetExecutor.Execute(packCommand, true);
		}

		private string Restore(string name, string version, string nugetSourceUrl, string destinationNupkgDirectory) {
			destinationNupkgDirectory = _fileSystem.GetCurrentDirectoryIfEmpty(destinationNupkgDirectory);
			return _workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				string nugetRestoreProjPath = Path.Combine(tempDirectory, NugetRestoreProjName);
				CreateNugetRestoreProj(nugetRestoreProjPath, name, version);
				string result = RestorePackage(nugetRestoreProjPath, nugetSourceUrl, destinationNupkgDirectory)
					.Replace(nugetRestoreProjPath, $"\"{destinationNupkgDirectory}\"");
				return result;
			});
		}

		private void RestoreToDirectory(string packageName, string version, string nugetSourceUrl,
			string destinationNupkgDirectory, Action<IEnumerable<string>> onRestored) {
			string result = string.Empty;
			_workingDirectoriesProvider.CreateTempDirectory(restoreTempDirectory =>{
				result = Restore(packageName, version, nugetSourceUrl, restoreTempDirectory)
					.Replace(restoreTempDirectory, $"{destinationNupkgDirectory}");
				_logger.WriteLine(result);
				IEnumerable<string> gzipPackedPackagesFiles = 
					_packageArchiver.FindGzipPackedPackagesFiles(restoreTempDirectory);
				onRestored(gzipPackedPackagesFiles);
			});
		}

		#endregion

		#region Methods: Public

		public void RestoreToNugetFileStorage(string packageName, string version, string nugetSourceUrl, 
				string destinationNupkgDirectory) {
			CheckArguments(packageName, version, nugetSourceUrl);
			string result = Restore(packageName, version, nugetSourceUrl, destinationNupkgDirectory);
			_logger.WriteLine(result);
		}

		public void RestoreToDirectory(string packageName, string version, string nugetSourceUrl,
				string destinationNupkgDirectory, bool overwrite) {
			CheckArguments(packageName, version, nugetSourceUrl);
			RestoreToDirectory(packageName, version, nugetSourceUrl, destinationNupkgDirectory,
				gzipPackedPackagesFiles => {
					_fileSystem.Copy(gzipPackedPackagesFiles, destinationNupkgDirectory, overwrite);
				});
		}

		public void RestoreToPackageStorage(string packageName, string version, string nugetSourceUrl,
				string destinationNupkgDirectory, bool overwrite) {
			CheckArguments(packageName, version, nugetSourceUrl);
			RestoreToDirectory(packageName, version, nugetSourceUrl, destinationNupkgDirectory,
				gzipPackedPackagesFiles => {
					_packageArchiver.Unpack(gzipPackedPackagesFiles, overwrite, destinationNupkgDirectory);
				});
		}

		#endregion

	}
}