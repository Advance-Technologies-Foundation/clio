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

		private string GetNuspecFileName(PackageInfo packageInfo) {
			return $"{packageInfo.Name}.{NuspecExtension}";
		}
		
		private string GetNuspecFilesSection(string nuspecFilesDirectory, PackageInfo packageInfo) {
			var sb = new StringBuilder();
			foreach (string filePath in packageInfo.FilePaths) {
				string target = filePath.Replace(nuspecFilesDirectory, string.Empty);
				string fileRecord = FileRecordTemplate
					.Replace("$src$", filePath)
					.Replace("$target$", target);
				sb.AppendLine(fileRecord);
			}
			return sb.ToString();
		}

		private string GetNuspecDependenciesSection(PackageInfo packageInfo, string version, 
				IDictionary<string, PackageInfo> packagesInfo) {
			var sb = new StringBuilder();
			foreach (string dependency in packageInfo.Depends) {
				if (!packagesInfo.ContainsKey(dependency)) {
					continue;
				}
				string dependencyVersion = DependencyVersionTemplate.Replace("$version$", version);
				string fileRecord = DependencyRecordTemplate
					.Replace("$id$", dependency)
					.Replace("$version$", dependencyVersion);
				sb.AppendLine(fileRecord);
			}
			return sb.ToString();
		}

		private string ReplaceMacro(string template, string version, PackageInfo packageInfo, string filesSection,
				string dependenciesSection) {
			return template.Replace("$id$", packageInfo.Name)
				.Replace("$version$", version)
				.Replace("$authors$", packageInfo.Maintainer)
				.Replace("$owners$", packageInfo.Maintainer)
				.Replace("$copyright$", $"Copyright {DateTime.Now.Year}")
				.Replace("$dependencies$", dependenciesSection)
				.Replace("$files$", filesSection);
		}

		private void CreateFromTpl(string filePath, string version, PackageInfo packageInfo,  string filesSection,
				string dependenciesSection) {
			string template = _templateProvider.GetTemplate(_packageNuspecFileName);
			string nuspecFileContent = ReplaceMacro(template, version, packageInfo, filesSection, dependenciesSection);
			File.WriteAllText(filePath, nuspecFileContent);
		}

		public void Create(string packagesPath, IDictionary<string, PackageInfo> packagesInfo, string version) {
			packagesPath.CheckArgumentNullOrWhiteSpace(nameof(packagesPath));
			packagesInfo.CheckArgumentNull(nameof(packagesInfo));
			version.CheckArgumentNullOrWhiteSpace(nameof(version));
			foreach (PackageInfo packageInfo in packagesInfo.Values) {
				string nuspecFilePath = Path.Combine(packagesPath, GetNuspecFileName(packageInfo));
				string filesSection = GetNuspecFilesSection(packagesPath, packageInfo);
				string dependenciesSection = GetNuspecDependenciesSection(packageInfo, version, packagesInfo);
				CreateFromTpl(nuspecFilePath, version, packageInfo, filesSection, dependenciesSection);
			}
		}
	}
}