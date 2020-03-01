using System;
using System.IO;

namespace Clio.Common
{

	public class WorkingDirectoriesProvider : IWorkingDirectoriesProvider
	{
		public string ExecutingDirectory => AppDomain.CurrentDomain.BaseDirectory;
		public string TemplateDirectory =>  Path.Combine(ExecutingDirectory, "tpl");
		public string BaseTempDirectory =>  Path.Combine(Path.GetTempPath(), "clio");

		public string GetTemplatePath(string templateName) {
			templateName.CheckArgumentNullOrWhiteSpace(nameof(templateName));
			return Path.Combine(TemplateDirectory, $"{templateName}.tpl");
		}

		public string CreateTempDirectory() {
			Directory.CreateDirectory(BaseTempDirectory);
			string directoryName = DateTime.Now.Ticks.ToString();
			string tempDirectory = Path.Combine(BaseTempDirectory, directoryName);
			Directory.CreateDirectory(tempDirectory);
			return tempDirectory;
		}

	}

}