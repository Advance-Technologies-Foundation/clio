using System;
using System.IO;
using System.Linq;
using Clio.Common;

namespace Clio.Project.NuGet
{

	#region Class: NugetPacker

	public class NugetPacker : INugetPacker
	{

		#region Fields: Private

		private const string NugetPackProjName = "NugetPackProj.csproj";
		private readonly IDotnetExecutor _dotnetExecutor;
		private readonly ITemplateProvider _templateProvider;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;
		private readonly ILogger _logger;

		#endregion

		#region Constructors: Public

		public NugetPacker(ITemplateProvider templateProvider, IDotnetExecutor dotnetExecutor, 
				IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem, ILogger logger) {
			dotnetExecutor.CheckArgumentNull(nameof(dotnetExecutor));
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			logger.CheckArgumentNull(nameof(logger));
			_dotnetExecutor = dotnetExecutor;
			_templateProvider = templateProvider;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
			_logger = logger;
		}

		#endregion

		#region Methods: Private

		private static void CheckArguments(string nuspecFilePath, string destinationNupkgDirectory) {
			nuspecFilePath.CheckArgumentNullOrWhiteSpace(nameof(nuspecFilePath));
			destinationNupkgDirectory.CheckArgumentNullOrWhiteSpace(nameof(destinationNupkgDirectory));
			if (!File.Exists(nuspecFilePath)) {
				throw new InvalidOperationException($"Invalid nuspec file path '{nuspecFilePath}'");
			}
		}

		private void CreateNugetPackProj(string nugetPackProjPath) {
			string template = _templateProvider.GetTemplate(NugetPackProjName);
			File.WriteAllText(nugetPackProjPath, template);
		}

		private string PackPackage(string nugetPackProjPath, string nuspecFilePath, string destinationNupkgDirectory) {
			string packCommand = $"pack \"{nugetPackProjPath}\" -p:NuspecFile=\"{nuspecFilePath}\"" + 
				$" --output \"{destinationNupkgDirectory}\" ";
			return _dotnetExecutor.Execute(packCommand, true);
		}

		private void DeleteTempNetstandardDirectoryIfExists(string destinationNupkgDirectory) {
			string netstandard20 = Path.Combine(destinationNupkgDirectory, "netstandard2.0");
			_fileSystem.DeleteDirectoryIfExists(netstandard20);
		}

		#endregion

		#region Methods: Public

		public string GetNupkgFileName(PackageInfo pkgInfo) {
			return $"{pkgInfo.Descriptor.Name}.{pkgInfo.Descriptor.PackageVersion}.{NugetConstants.NupkgExtension}";
		}

		public void Pack(string nuspecFilePath, string destinationNupkgDirectory) {
			CheckArguments(nuspecFilePath, destinationNupkgDirectory);
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				string nugetPackProjPath = Path.Combine(tempDirectory, NugetPackProjName);
				CreateNugetPackProj(nugetPackProjPath);
				string packResult = PackPackage(nugetPackProjPath, nuspecFilePath, destinationNupkgDirectory);
				DeleteTempNetstandardDirectoryIfExists(destinationNupkgDirectory);
				_logger.WriteLine(packResult);
			});
		}

		#endregion

	}

	#endregion

}