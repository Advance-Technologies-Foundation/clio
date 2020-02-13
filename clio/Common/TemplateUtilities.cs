using System;
using System.IO;

namespace Clio.Common
{
	public class TemplateUtilities : ITemplateUtilities
	{
		private string ExecutingDirectorybyAppDomain => AppDomain.CurrentDomain.BaseDirectory;

		private string GetAbsoluteTemplatePath(string relativeTplPath) {
			string fullTplPath = Path.Combine(ExecutingDirectorybyAppDomain, relativeTplPath);
			if (!File.Exists(fullTplPath)) {
				throw new InvalidOperationException($"Invalid template file path '{fullTplPath}'");
			}
			return fullTplPath;
		}
		
		public string GetTemplate(string relativeTplPath) {
			if (string.IsNullOrWhiteSpace(relativeTplPath)) {
				throw new ArgumentNullException(nameof(relativeTplPath));
			}
			string absoluteTplPath = GetAbsoluteTemplatePath(relativeTplPath);
			return File.ReadAllText(absoluteTplPath);
		}
	}
}