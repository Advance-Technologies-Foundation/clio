using System;
using System.IO;
using System.Linq;
using Clio.Common;

namespace Clio.Project.NuGet
{
	public class NugetPacker : INugetPacker
	{
		public const string NupkgExtension = "nupkg";
		
		private const string NugetPackProjName = "NugetPackProj.csproj";
		private readonly IDotnetExecutor _dotnetExecutor;
		private readonly ITemplateProvider _templateProvider;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		
		public NugetPacker(ITemplateProvider templateProvider, IDotnetExecutor dotnetExecutor, 
				IWorkingDirectoriesProvider workingDirectoriesProvider) {
			dotnetExecutor.CheckArgumentNull(nameof(dotnetExecutor));
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			_dotnetExecutor = dotnetExecutor;
			_templateProvider = templateProvider;
			_workingDirectoriesProvider = workingDirectoriesProvider;
		}

		private void CreateNugetPackProj(string nugetPackProjPath) {
			string template = _templateProvider.GetTemplate(NugetPackProjName);
			File.WriteAllText(nugetPackProjPath, template);
		}

		private string GetNupkgFilePath(string nugetPackProjPath) {
			var fileInfo = new FileInfo(nugetPackProjPath);
			string nupkgFileDirectory = Path.Combine(fileInfo.DirectoryName, "bin", "Debug");
			string nupkgFilePath = Directory
				.EnumerateFiles(nupkgFileDirectory, $"*.{NugetPacker.NupkgExtension}")
				.FirstOrDefault();
			if (string.IsNullOrWhiteSpace(nupkgFilePath)) {
				throw new InvalidOperationException("Error packing nuget package");
			}
			return nupkgFilePath;
		}

		private void PackPackage(string nugetPackProjPath, string nuspecFilePath) {
			string packCommand = $"pack \"{nugetPackProjPath}\" -p:NuspecFile=\"{nuspecFilePath}\"";
			_dotnetExecutor.Execute(packCommand, true);
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

		public string GetNupkgFileName(PackageInfo packageInfo) {
			return $"{packageInfo.Name}.{packageInfo.PackageVersion}.{NupkgExtension}";
		}

		public void Pack(string nuspecFilePath, string destinationNupkgFilePath) {
			nuspecFilePath.CheckArgumentNullOrWhiteSpace(nameof(nuspecFilePath));
			destinationNupkgFilePath.CheckArgumentNullOrWhiteSpace(nameof(destinationNupkgFilePath));
			if (!File.Exists(nuspecFilePath)) {
				throw new InvalidOperationException($"Invalid nuspec file path '{nuspecFilePath}'");
			}
			string tempDirectory = _workingDirectoriesProvider.CreateTempDirectory();
			try {
				string nugetPackProjPath = Path.Combine(tempDirectory, NugetPackProjName);
				CreateNugetPackProj(nugetPackProjPath);
				PackPackage(nugetPackProjPath, nuspecFilePath);
				string sourceNupkgFilePath = GetNupkgFilePath(nugetPackProjPath);
				CopyNupkgFileToDestinationDirectory(sourceNupkgFilePath, destinationNupkgFilePath);
			} finally {
				_workingDirectoriesProvider.SafeDeleteTempDirectory(tempDirectory);
			}
		}

	}
}