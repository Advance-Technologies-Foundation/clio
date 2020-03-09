using System;
using System.IO;

namespace Clio.Common
{
	public class TemplateProvider : ITemplateProvider
	{
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;

		public TemplateProvider(IWorkingDirectoriesProvider workingDirectoriesProvider) {
			workingDirectoriesProvider.CheckArgumentNull(nameof(workingDirectoriesProvider));
			_workingDirectoriesProvider = workingDirectoriesProvider;
		}

		public string GetTemplate(string templateName) {
			templateName.CheckArgumentNullOrWhiteSpace(nameof(templateName));
			string templatePath = _workingDirectoriesProvider.GetTemplatePath(templateName); 
			if (!File.Exists(templatePath)) {
				throw new InvalidOperationException($"Invalid template file path '{templatePath}'");
			}
			return File.ReadAllText(templatePath);
		}
	}
}