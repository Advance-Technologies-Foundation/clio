using Castle.Components.DictionaryAdapter;
using System;
using System.IO;
using System.Management.Automation;

namespace Clio.Common
{

	#region Class: WorkingDirectoriesProvider

	public class WorkingDirectoriesProvider : IWorkingDirectoriesProvider
	{

		private readonly ILogger _logger;
		private readonly System.IO.Abstractions.IFileSystem _fileSystem;

		public WorkingDirectoriesProvider(ILogger logger, System.IO.Abstractions.IFileSystem fileSystem){
			_logger = logger;
			_fileSystem = fileSystem;
		}
		#region Properties: Public
		
		public static string _executingDirectory;
		
		public string ExecutingDirectory  => _executingDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
		public string TemplateDirectory =>  Path.Combine(ExecutingDirectory, "tpl");
		public string BaseTempDirectory {
			get {
				string tempDir = Environment.GetEnvironmentVariable("CLIO_WORKING_DIRECTORY");
				string path = Path.Combine(string.IsNullOrEmpty(tempDir) 
					? Path.GetTempPath() 
					: tempDir, "clio");

				return path;
			}
		}

		public string CurrentDirectory => _fileSystem.Directory.GetCurrentDirectory();

		#endregion

		#region Methods: Public

		public string GetTemplatePath(string templateName) {
			templateName.CheckArgumentNullOrWhiteSpace(nameof(templateName));
			return Path.Combine(TemplateDirectory, $"{templateName}.tpl");
		}

		public string GetTemplateFolderPath(string templateFolderName) {
			templateFolderName.CheckArgumentNullOrWhiteSpace(nameof(templateFolderName));
			return Path.Combine(TemplateDirectory, templateFolderName);
		}

		public string CreateTempDirectory() {
			_fileSystem.Directory.CreateDirectory(BaseTempDirectory);
			var tempDirectoryPath = GenerateTempDirectoryPath();
			string tempDirectory = Path.Combine(BaseTempDirectory, tempDirectoryPath);
			_fileSystem.Directory.CreateDirectory(tempDirectory);
			return tempDirectory;
		}

		public void CreateTempDirectory(Action<string> onCreated) {
			string tempDirectoryPath = CreateTempDirectory();
			try {
				onCreated(tempDirectoryPath);
			} finally {
				DeleteDirectoryIfExists(tempDirectoryPath);
			}
		}

		public T CreateTempDirectory<T>(Func<string, T> onCreated) {
			string tempDirectoryPath = CreateTempDirectory();
			try {
				return onCreated(tempDirectoryPath);
			} finally {
				DeleteDirectoryIfExists(tempDirectoryPath);
			}
		}

		public void DeleteDirectoryIfExists(string path) {
			path.CheckArgumentNull(nameof(path));
			if (_fileSystem.Directory.Exists(path)) {
				_fileSystem.Directory.Delete(path, true);
			}
		}

		public string GenerateTempDirectoryPath() {
			return Guid.NewGuid().ToString("N");
		}

		#endregion

	}

	#endregion

}