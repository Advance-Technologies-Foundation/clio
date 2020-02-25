using System.Collections.Generic;
using System.IO;
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

		private void Pack(string nugetPackProjPath, IEnumerable<string> nuspecFilesPaths) {
			foreach (string nuspecFilePath in nuspecFilesPaths) {
				string packCommand = $"pack {nugetPackProjPath} -p:NuspecFile={nuspecFilePath}";
				_dotnetExecutor.Execute(packCommand, true);
			}
		}

		private string GetNupkgFilesDirectory(string nugetPackProjPath) {
			var fileInfo = new FileInfo(nugetPackProjPath);
			return Path.Combine(fileInfo.DirectoryName, "bin", "Debug");
		}
 
		private void CopyNupkgFilesToDestinationDirectory(string nupkgFilesDirectory, string destinationNupkgDirectory) {
			if (!Directory.Exists(nupkgFilesDirectory)) {
				return;
			}
			IEnumerable<string> nupkgFilesPaths = Directory.EnumerateFiles(nupkgFilesDirectory, $"*.{NupkgExtension}");
			foreach (string nupkgFilePath in  nupkgFilesPaths) {
				var fileInfo = new FileInfo(nupkgFilePath);
				string destinationFilePath = Path.Combine(destinationNupkgDirectory, fileInfo.Name);
				if (File.Exists(destinationFilePath)) {
					File.Delete(destinationFilePath);
				}
				File.Copy(nupkgFilePath, destinationFilePath);
			}
		}

		public void Pack(IEnumerable<string> nuspecFilesPaths, string destinationNupkgDirectory) {
			nuspecFilesPaths.CheckArgumentNull(nameof(nuspecFilesPaths));
			destinationNupkgDirectory.CheckArgumentNullOrWhiteSpace(nameof(destinationNupkgDirectory));
			string tempDirectory = _workingDirectoriesProvider.CreateTempDirectory();
			try {
				string nugetPackProjPath = Path.Combine(tempDirectory, NugetPackProjName);
				CreateNugetPackProj(nugetPackProjPath);
				Pack(nugetPackProjPath, nuspecFilesPaths);
				string nupkgFilesDirectory = GetNupkgFilesDirectory(nugetPackProjPath);
				CopyNupkgFilesToDestinationDirectory(nupkgFilesDirectory, destinationNupkgDirectory);
			} finally {
				_workingDirectoriesProvider.SafeDeleteTempDirectory(tempDirectory);
			}
		}

	}
}