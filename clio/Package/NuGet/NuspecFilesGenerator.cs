using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Clio.Common;

namespace Clio.Project.NuGet
{
	public class NuspecFilesGenerator : INuspecFilesGenerator
	{
		public const string NuspecExtension = "nuspec";

		private const string FileRecordTemplate = "    <file src=\"$src$\" target=\"tools$target$\" />";
		private const string DependencyRecordTemplate = "      <dependency id=\"$id$\"$version$ />";
		private const string DependencyVersionTemplate = " version=\"$version$\"";
		private readonly string _packageNuspecFileName = $"Package.{NuspecExtension}";
		private readonly ITemplateProvider _templateProvider;

		public NuspecFilesGenerator(ITemplateProvider templateProvider) {
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			_templateProvider = templateProvider;
		}

		private string GetNuspecFilesSection(PackageInfo packageInfo) {
			var sb = new StringBuilder();
			foreach (string filePath in packageInfo.FilePaths) {
				string target = filePath.Replace(packageInfo.PackagePath, string.Empty);
				string fileRecord = FileRecordTemplate
					.Replace("$src$", filePath)
					.Replace("$target$", target);
				sb.AppendLine(fileRecord);
			}
			return sb.ToString();
		}

		private string GetNuspecDependenciesSection(IEnumerable<DependencyInfo> dependencies) {
			var sb = new StringBuilder();
			foreach (DependencyInfo dependency in dependencies) {
				string dependencyVersion = DependencyVersionTemplate
					.Replace("$version$", dependency.PackageVersion);
				string fileRecord = DependencyRecordTemplate
					.Replace("$id$", dependency.Name)
					.Replace("$version$", dependencyVersion);
				sb.AppendLine(fileRecord);
			}
			return sb.ToString();
		}

		private string ReplaceMacro(string template, PackageInfo packageInfo, string filesSection,
				string dependenciesSection) {
			return template.Replace("$id$", packageInfo.Name)
				.Replace("$version$", packageInfo.PackageVersion)
				.Replace("$authors$", packageInfo.Maintainer)
				.Replace("$owners$", packageInfo.Maintainer)
				.Replace("$copyright$", $"Copyright {DateTime.Now.Year}")
				.Replace("$dependencies$", dependenciesSection)
				.Replace("$files$", filesSection);
		}

		private void CreateFromTemplate(PackageInfo packageInfo, string filesSection, string dependenciesSection,
				string filePath) {
			string template = _templateProvider.GetTemplate(_packageNuspecFileName);
			string nuspecFileContent = ReplaceMacro(template, packageInfo, filesSection, dependenciesSection);
			File.WriteAllText(filePath, nuspecFileContent);
		}

		public string GetNuspecFileName(PackageInfo packageInfo) {
			return $"{packageInfo.Name}.{packageInfo.PackageVersion}.{NuspecExtension}";
		}

		public void Create(PackageInfo packageInfo, IEnumerable<DependencyInfo> dependencies, string nuspecFilePath) {
			packageInfo.CheckArgumentNull(nameof(packageInfo));
			dependencies.CheckArgumentNull(nameof(dependencies));
			nuspecFilePath.CheckArgumentNullOrWhiteSpace(nameof(nuspecFilePath));
			string filesSection = GetNuspecFilesSection(packageInfo);
			string dependenciesSection = GetNuspecDependenciesSection(dependencies);
			CreateFromTemplate(packageInfo, filesSection, dependenciesSection, nuspecFilePath);
		}

	}

}