using System;
using System.IO;
using System.Linq;
using Clio.Common;

namespace Clio.Project.NuGet
{
	public class NugetPacker : INugetPacker
	{

		private const string NugetPackProjName = "NugetPackProj.csproj";
		private readonly IDotnetExecutor _dotnetExecutor;
		private readonly ITemplateProvider _templateProvider;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;
		private readonly ILogger _logger;

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

		private static void CheckArguments(string nuspecFilePath, string destinationNupkgFilePath) {
			nuspecFilePath.CheckArgumentNullOrWhiteSpace(nameof(nuspecFilePath));
			destinationNupkgFilePath.CheckArgumentNullOrWhiteSpace(nameof(destinationNupkgFilePath));
			if (!File.Exists(nuspecFilePath)) {
				throw new InvalidOperationException($"Invalid nuspec file path '{nuspecFilePath}'");
			}
		}

		private void CreateNugetPackProj(string nugetPackProjPath) {
			string template = _templateProvider.GetTemplate(NugetPackProjName);
			File.WriteAllText(nugetPackProjPath, template);
		}

		private string GetNupkgFilePath(string nugetPackProjPath) {
			var fileInfo = new FileInfo(nugetPackProjPath);
			string nupkgFileDirectory = Path.Combine(fileInfo.DirectoryName, "bin", "Debug");
			string nupkgFilePath = Directory
				.EnumerateFiles(nupkgFileDirectory, $"*.{NugetConstants.NupkgExtension}")
				.FirstOrDefault();
			if (string.IsNullOrWhiteSpace(nupkgFilePath)) {
				throw new InvalidOperationException("Error packing nuget package");
			}
			return nupkgFilePath;
		}

		private string PackPackage(string nugetPackProjPath, string nuspecFilePath) {
			string packCommand = $"pack \"{nugetPackProjPath}\" -p:NuspecFile=\"{nuspecFilePath}\"";
			return _dotnetExecutor.Execute(packCommand, true);
		}
 
		private void CopyNupkgFileToDestinationDirectory(string sourceNupkgFilePath, string destinationNupkgFilePath) {
			if (!File.Exists(sourceNupkgFilePath)) {
				throw new InvalidOperationException($"Invalid nupkg file path '{sourceNupkgFilePath}'");
			}
			if (File.Exists(destinationNupkgFilePath)) {
				File.Delete(destinationNupkgFilePath);
			}
			File.Copy(sourceNupkgFilePath, destinationNupkgFilePath);
		}

		private string ReplaceInOutputResult(string outputResult, string nugetPackProjPath, string sourceNupkgFilePath, 
				string destinationNupkgFilePath) {
			return outputResult.Replace(nugetPackProjPath, "nuget file")
				.Replace(sourceNupkgFilePath, destinationNupkgFilePath);
		}

		public string GetNupkgFileName(PackageInfo packageInfo) {
			return $"{packageInfo.Name}.{packageInfo.PackageVersion}.{NugetConstants.NupkgExtension}";
		}

		public void Pack(string nuspecFilePath, string destinationNupkgFilePath) {
			CheckArguments(nuspecFilePath, destinationNupkgFilePath);
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				string nugetPackProjPath = Path.Combine(tempDirectory, NugetPackProjName);
				CreateNugetPackProj(nugetPackProjPath);
				string result = PackPackage(nugetPackProjPath, nuspecFilePath);
				string sourceNupkgFilePath = GetNupkgFilePath(nugetPackProjPath);
				CopyNupkgFileToDestinationDirectory(sourceNupkgFilePath, destinationNupkgFilePath);
				result = ReplaceInOutputResult(result, nugetPackProjPath, sourceNupkgFilePath,
					destinationNupkgFilePath);
				_logger.WriteLine(result);
			});
		}

	}
}