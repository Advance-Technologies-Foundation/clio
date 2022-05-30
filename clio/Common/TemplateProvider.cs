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

		public void CopyTemplateFolder(string templateFolderName, string destinationPath) {
			templateFolderName.CheckArgumentNullOrWhiteSpace(nameof(templateFolderName));
			destinationPath.CheckArgumentNullOrWhiteSpace(nameof(destinationPath));
			string templatePath = _workingDirectoriesProvider.GetTemplateFolderPath(templateFolderName);
			_fileSystem.CopyDirectory(templatePath, destinationPath, true);
			DeletePlaceholder(destinationPath);
		}

		#endregion

	}

	#endregion

}