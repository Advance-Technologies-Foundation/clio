using System;
using System.IO;

namespace Clio.Common
{

	#region Class: WorkingDirectoriesProvider

	public class WorkingDirectoriesProvider : IWorkingDirectoriesProvider
	{

		private readonly ILogger _logger;

		public WorkingDirectoriesProvider(ILogger logger){
			_logger = logger;
		}
		#region Properties: Public

		public string ExecutingDirectory => AppDomain.CurrentDomain.BaseDirectory;
		public string TemplateDirectory =>  Path.Combine(ExecutingDirectory, "tpl");
		public string BaseTempDirectory {
			get {
				string tempDir = Environment.GetEnvironmentVariable("CLIO_WORKING_DIRECTORY");
				string path = Path.Combine(string.IsNullOrEmpty(tempDir) 
					? Path.GetTempPath() 
					: tempDir, "clio");
#if DEBUG
				_logger.WriteInfo($"Clio temptDir path: {path}");
#endif
				return path;
			}
		}

		public string CurrentDirectory => Directory.GetCurrentDirectory();

		#endregion

		#region Methods: Private

		private void DeleteDirectoryIfExists(string path) {
			path.CheckArgumentNull(nameof(path));
			if (Directory.Exists(path)) {
				Directory.Delete(path, true);
			}
		}

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
			Directory.CreateDirectory(BaseTempDirectory);
			string directoryName = DateTime.Now.Ticks.ToString();
			string tempDirectory = Path.Combine(BaseTempDirectory, directoryName);
			Directory.CreateDirectory(tempDirectory);
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

		#endregion

	}

	#endregion

}