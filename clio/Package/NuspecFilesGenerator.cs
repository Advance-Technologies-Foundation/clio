using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Clio.Common;

namespace Clio
{
	public class NuspecFilesGenerator : INuspecFilesGenerator
	{
		private const string NuspecExtension = "nuspec";
		private const string PackageNuspecName = "Package.nuspec";
		private const string FileRecordTemplate = "    <file src=\"$src$\" target=\"tools$target$\" />";
		private const string DependencyRecordTemplate = "      <dependency id=\"$id$\"$version$ />";
		private const string DependencyVersionTemplate = " version=\"$version$\"";
		private static readonly string PackageNuspecTpl = $"tpl{Path.DirectorySeparatorChar}{PackageNuspecName}.tpl";
		private readonly ITemplateProvider _templateProvider;

		public NuspecFilesGenerator(ITemplateProvider templateProvider) {
			_templateProvider = templateProvider ?? throw new ArgumentException(nameof(templateProvider));
		}

		private string GetNuspecFileName(PackageInfo packageInfo) {
			return $"{packageInfo.Name}.{NuspecExtension}";
		}
		
		private string GetFiles(string nuspecFilesDirectory, PackageInfo packageInfo) {
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

		private string GetDependencies(PackageInfo packageInfo, string version, 
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

		private string ReplaceMacro(string template, string version, PackageInfo packageInfo, string files,
				string dependencies) {
			return template.Replace("$id$", packageInfo.Name)
				.Replace("$version$", version)
				.Replace("$authors$", packageInfo.Maintainer)
				.Replace("$owners$", packageInfo.Maintainer)
				.Replace("$copyright$", $"Copyright {DateTime.Now.Year}")
				.Replace("$dependencies$", dependencies)
				.Replace("$files$", files);
		}

		private void CreateFromTpl(string filePath, string version, PackageInfo packageInfo,  string files,
				string dependencies) {
			string template = _templateProvider.GetTemplate(PackageNuspecTpl);
			string nuspecFileContent = ReplaceMacro(template, version, packageInfo, files, dependencies);
			File.WriteAllText(filePath, nuspecFileContent);
		}

		public void Create(string nuspecFilesDirectory, IDictionary<string, PackageInfo> packagesInfo, string version) {
			if (string.IsNullOrWhiteSpace(nuspecFilesDirectory)) {
				throw new ArgumentNullException(nameof(nuspecFilesDirectory));
			}
			if (packagesInfo == null) {
				throw new ArgumentNullException(nameof(packagesInfo));
			}
			if (string.IsNullOrWhiteSpace(version)) {
				throw new ArgumentNullException(nameof(version));
			}
			foreach (PackageInfo packageInfo in packagesInfo.Values) {
				string filePath = Path.Combine(nuspecFilesDirectory, GetNuspecFileName(packageInfo));
				string files = GetFiles(nuspecFilesDirectory, packageInfo);
				string dependencies = GetDependencies(packageInfo, version, packagesInfo);
				CreateFromTpl(filePath, version, packageInfo, files, dependencies);
			}
		}
	}
}