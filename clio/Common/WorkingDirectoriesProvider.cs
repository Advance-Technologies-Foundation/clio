using System;
using System.IO;

namespace Clio.Common
{

	public class WorkingDirectoriesProvider : IWorkingDirectoriesProvider
	{
		public string ExecutingDirectory => AppDomain.CurrentDomain.BaseDirectory;
		public string TemplateDirectory =>  Path.Combine(ExecutingDirectory, "tpl");
		public string BaseTempDirectory =>  Path.GetTempPath();

		public string GetTemplatePath(string templateName) {
			templateName.CheckArgumentNullOrWhiteSpace(nameof(templateName));
			return Path.Combine(TemplateDirectory, $"{templateName}.tpl");
		}

		public string CreateTempDirectory() {
			string directoryName = DateTime.Now.Ticks.ToString();
			string tempDirectory = Path.Combine(BaseTempDirectory, directoryName);
			Directory.CreateDirectory(tempDirectory);
			return tempDirectory;
		}

		public void SafeDeleteTempDirectory(string tempDirectory) {
			tempDirectory.CheckArgumentNull(nameof(tempDirectory));
			if (Directory.Exists(tempDirectory)) {
				Directory.Delete(tempDirectory, true);
			}
		}

	}

}