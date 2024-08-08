using System.Collections.Generic;
using System.Linq;

namespace Clio.Common
{
	using System;
	using System.IO;

	#region Class: TemplateProvider

	public class TemplateProvider : ITemplateProvider
	{

		#region Fields: Private

		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _fileSystem;

		#endregion

		#region Constructors: Public

		public TemplateProvider(IWorkingDirectoriesProvider workingDirectoriesProvider, IFileSystem fileSystem) {
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			fileSystem.CheckArgumentNull(nameof(fileSystem));
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_fileSystem = fileSystem;
		}

		#endregion

		#region Methods: Private

		private void DeletePlaceholder(string directoryPath) {
			string[] placeholderPaths = _fileSystem
				.GetFiles(directoryPath, "placeholder.txt", SearchOption.AllDirectories);
			foreach (string placeholderPath in placeholderPaths) {
				_fileSystem.DeleteFile(placeholderPath);
			}
		}

		#endregion

		#region Methods: Public

		public string GetTemplate(string templateName) {
			templateName.CheckArgumentNullOrWhiteSpace(nameof(templateName));
			string templatePath = _workingDirectoriesProvider.GetTemplatePath(templateName);
			if (!File.Exists(templatePath)) {
				throw new InvalidOperationException($"Invalid template file path '{templatePath}'");
			}
			return File.ReadAllText(templatePath);
		}

		public void CopyTemplateFolder(string templateFolderName, string destinationPath, string creatioVersion = "",
			string group = "", bool overrideFolder = true) {
			templateFolderName.CheckArgumentNullOrWhiteSpace(nameof(templateFolderName));
			destinationPath.CheckArgumentNullOrWhiteSpace(nameof(destinationPath));
			string templatePath = GetCompatibleVersionTemplatePath(templateFolderName, creatioVersion, group);
			_fileSystem.CopyDirectory(templatePath, destinationPath, overrideFolder);
		}
		
		public void CopyTemplateFolder(string templateFolderName, string destinationPath, Dictionary<string, string> macrosValues) {
			templateFolderName.CheckArgumentNullOrWhiteSpace(nameof(templateFolderName));
			destinationPath.CheckArgumentNullOrWhiteSpace(nameof(destinationPath));
			
			var templateDir = Path.Combine(_workingDirectoriesProvider.TemplateDirectory, templateFolderName);
			var files = _fileSystem.GetFiles(templateDir, "*.tpl", SearchOption.AllDirectories);
			
			foreach (var file in files) {
				
				var content = _fileSystem.ReadAllText(file);
				content = ReplaceMacrosInText(content, macrosValues);
				var relativePath = file.Replace(templateDir, string.Empty).TrimStart(Path.DirectorySeparatorChar);
				var destinationFilePath = Path.Combine(destinationPath, relativePath);
				destinationFilePath = destinationFilePath.Replace(".tpl", string.Empty);
				destinationFilePath = ReplaceMacrosInText(destinationFilePath, macrosValues);
				_fileSystem.CreateDirectoryIfNotExists(Path.GetDirectoryName(destinationFilePath));
				_fileSystem.WriteAllTextToFile(destinationFilePath, content);
			}
		}

		private string ReplaceMacrosInText(string content, Dictionary<string, string> macrosValues){
			foreach (var macro in macrosValues) {
				content = content.Replace(macro.Key, macro.Value);
			}
			return content;
		}

		private string GetCompatibleVersionTemplatePath(string templateName, string creatioVersion = "",
			string group = "") {
			bool groupExists = !string.IsNullOrWhiteSpace(group);
			if (!groupExists && string.IsNullOrWhiteSpace(creatioVersion)) {
				return _workingDirectoriesProvider.GetTemplateFolderPath(templateName);
			}
			string root = groupExists ? group : templateName;
			string rootPath = _workingDirectoriesProvider.GetTemplateFolderPath(root);
			DirectoryInfo[] versions = new DirectoryInfo(rootPath).GetDirectories();

			List<Version> availableVersions = new List<Version>();
			foreach (var item in versions) {
				if (Version.TryParse(item.Name, out Version version)) {
					availableVersions.Add(version);
				}
			}
			availableVersions.Sort();
			Version compatibleVersion = availableVersions.FindLast(v => v <= new Version(creatioVersion));
			if (compatibleVersion is null) {
				throw new ArgumentException($"Minimum compatible version is {availableVersions.First().ToString()}",
					"version");
			}
			return Path.Combine(rootPath, compatibleVersion.ToString(), groupExists ? templateName : string.Empty);
		}

		public string[] GetTemplateDirectories(string templateCode) {
			var templateFolder = _workingDirectoriesProvider.GetTemplateFolderPath(templateCode);
			return _fileSystem.GetDirectories(templateFolder);
		}

		
		
		#endregion

	}

	#endregion
}