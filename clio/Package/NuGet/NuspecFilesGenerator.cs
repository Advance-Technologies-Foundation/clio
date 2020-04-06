using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Clio.Common;

namespace Clio.Project.NuGet
{
	public class NuspecFilesGenerator : INuspecFilesGenerator
	{

		private const string FileRecordTemplate = "    <file src=\"$src$\" target=\"tools\\$target$\" />";
		private const string DependencyRecordTemplate = "      <dependency id=\"$id$\"$version$ />";
		private const string DependencyVersionTemplate = " version=\"$version$\"";
		private readonly string _packageNuspecFileName = $"Package.{NugetConstants.NuspecExtension}";
		private readonly ITemplateProvider _templateProvider;

		public NuspecFilesGenerator(ITemplateProvider templateProvider) {
			templateProvider.CheckArgumentNull(nameof(templateProvider));
			_templateProvider = templateProvider;
		}

		private static void CheckArguments(PackageInfo packageInfo, IEnumerable<PackageDependency> dependencies, 
			string packedPackagePath, string nuspecFilePath) {
			packageInfo.CheckArgumentNull(nameof(packageInfo));
			dependencies.CheckArgumentNull(nameof(dependencies));
			packedPackagePath.CheckArgumentNullOrWhiteSpace(nameof(packedPackagePath));
			nuspecFilePath.CheckArgumentNullOrWhiteSpace(nameof(nuspecFilePath));
		}
		
		private string GetNuspecFilesSection(string packedPackagePath) {
			var compressedPackageFileInfo = new FileInfo(packedPackagePath);
			return FileRecordTemplate
				.Replace("$src$", packedPackagePath)
				.Replace("$target$", compressedPackageFileInfo.Name);
		}

		private string GetNuspecDependenciesSection(IEnumerable<PackageDependency> dependencies) {
			var sb = new StringBuilder();
			foreach (PackageDependency dependency in dependencies) {
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
			return template.Replace("$id$", packageInfo.Descriptor.Name)
				.Replace("$version$", packageInfo.Descriptor.PackageVersion)
				.Replace("$authors$", packageInfo.Descriptor.Maintainer)
				.Replace("$owners$", packageInfo.Descriptor.Maintainer)
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

		public string GetNuspecFileName(PackageInfo pkgInfo) {
			return $"{pkgInfo.Descriptor.Name}.{pkgInfo.Descriptor.PackageVersion}.{NugetConstants.NuspecExtension}";
		}

		public void Create(PackageInfo packageInfo, IEnumerable<PackageDependency> dependencies,
				string packedPackagePath, string nuspecFilePath) {
			CheckArguments(packageInfo, dependencies, packedPackagePath, nuspecFilePath);
			string filesSection = GetNuspecFilesSection(packedPackagePath);
			string dependenciesSection = GetNuspecDependenciesSection(dependencies);
			CreateFromTemplate(packageInfo, filesSection, dependenciesSection, nuspecFilePath);
		}

	}

}