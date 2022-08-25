using Clio.Common;
using Newtonsoft.Json;
using System;
using System.IO;

namespace Clio
{
	public class CreatioPackage
	{

		public const string DescriptorName = "descriptor.json";
		public const string PropertiesDirName = "Properties";
		public const string CsprojExtension = "csproj";
		public const string SlnExtension = "sln";
		public const string PackageConfigName = "packages.config";
		public const string AssemblyInfoName = "AssemblyInfo.cs";
		public const string PlaceholderFileName = "placeholder.txt";
		public const string IgnoreFileName = "clioignore";


		public static string EditProjTpl => $"tpl{Path.DirectorySeparatorChar}EditProj.{CsprojExtension}.tpl";
		public static string PackageConfigTpl => $"tpl{Path.DirectorySeparatorChar}{PackageConfigName}.tpl";
		public static string AssemblyInfoTpl => $"tpl{Path.DirectorySeparatorChar}{AssemblyInfoName}.tpl";
		public static string IgnoreFileTpl => $"tpl{Path.DirectorySeparatorChar}package{Path.DirectorySeparatorChar}{IgnoreFileName}";

		private readonly string[] _pkgDirectories = { "Assemblies", "Data", "Schemas", "SqlScripts", "Resources", "Files", "Files\\cs" };

		private static string DescriptorTpl => $"tpl{Path.DirectorySeparatorChar}{DescriptorName}.tpl";
		private static string ProjTpl => $"tpl{Path.DirectorySeparatorChar}Proj.{CsprojExtension}.tpl";
		private string ProjectFileName => $"{PackageName}.{CsprojExtension}";
		private string SolutionName => PackageName;
		private string SolutionFileName => $"{SolutionName}.{SlnExtension}";


		public string PackageName { get; }

		public string Maintainer { get; }

		public Guid ProjectId { get; protected set; }

		public string FullPath { get; protected set; }

		private DateTime _createdOn;
		public DateTime CreatedOn
		{
			get => _createdOn;
			protected set => _createdOn = GetDateTimeTillSeconds(value);
		}

		protected CreatioPackage(string packageName, string maintainer) {
			PackageName = packageName;
			Maintainer = maintainer;
			CreatedOn = DateTime.UtcNow;
			FullPath = Path.Combine(Environment.CurrentDirectory, packageName);
		}

		private static DateTime GetDateTimeTillSeconds(DateTime dateTime) {
			return dateTime.AddTicks(-(dateTime.Ticks % TimeSpan.TicksPerSecond));
		}

		private static string ToJsonMsDate(DateTime date) {
			JsonSerializerSettings microsoftDateFormatSettings = new JsonSerializerSettings {
				DateFormatHandling = DateFormatHandling.MicrosoftDateFormat
			};
			return JsonConvert.SerializeObject(date, microsoftDateFormatSettings).Replace("\"", "").Replace("\\", "");
		}

		private string ReplaceMacro(string text) {
			return text.Replace("$safeprojectname$", PackageName)
				.Replace("$userdomain$", Maintainer)
				.Replace("$guid1$", ProjectId.ToString())
				.Replace("$year$", CreatedOn.Year.ToString())
				.Replace("$modifiedon$", ToJsonMsDate(CreatedOn));
		}

		private bool GetTplPath(string tplPath, out string fullPath) {
			if (File.Exists(tplPath)) {
				fullPath = tplPath;
				return true;
			}
			var envPath = GetExecutingDirectorybyAppDomain();
			if (!string.IsNullOrEmpty(envPath)) {
				fullPath = Path.Combine(envPath, tplPath);
				return true;
			}
			fullPath = null;
			return false;
		}

		private void ExecuteDotnetCommand(string command) {
			IProcessExecutor processExecutor = new ProcessExecutor();
			IDotnetExecutor dotnetExecutor = new DotnetExecutor(processExecutor);
			dotnetExecutor.Execute(command, true, FullPath);
		}

		public static string GetExecutingDirectorybyAppDomain() {
			string path = AppDomain.CurrentDomain.BaseDirectory;
			return path;
		}

		private bool CreateFromTpl(string tplPath, string filePath) {
			if (GetTplPath(tplPath, out string fullTplPath)) {
				var text = ReplaceMacro(File.ReadAllText(fullTplPath));
				File.WriteAllText(filePath, text);
				return true;
			}
			return false;
		}

		private void AddPlaceholderFile(string dirPath) {
			var placeholderPath = Path.Combine(dirPath, PlaceholderFileName);
			File.Create(placeholderPath).Dispose();
		}

		protected CreatioPackage CreatePkgDescriptor() {
			var filePath = Path.Combine(FullPath, DescriptorName);
			CreateFromTpl(DescriptorTpl, filePath);
			return this;
		}

		protected CreatioPackage CreateProj() {
			var filePath = Path.Combine(FullPath, ProjectFileName);
			CreateFromTpl(ProjTpl, filePath);
			return this;
		}

		protected CreatioPackage CreateSolution() {
			ExecuteDotnetCommand($"new sln -n {SolutionName}");
			ExecuteDotnetCommand($"sln {SolutionFileName} add {ProjectFileName}");
			return this;
		}

		protected CreatioPackage CreatePackageConfig() {
			var filePath = Path.Combine(FullPath, PackageConfigName);
			CreateFromTpl(PackageConfigTpl, filePath);
			return this;
		}

		protected CreatioPackage CreateEmptyClass() {
			var filePath = Path.Combine(FullPath, "Files\\cs", "EmptyClass.cs");
			File.Create(filePath).Dispose();
			return this;
		}

		protected CreatioPackage CreateAssemblyInfo() {
			Directory.CreateDirectory(Path.Combine(FullPath, PropertiesDirName));
			var filePath = Path.Combine(FullPath, PropertiesDirName, AssemblyInfoName);
			CreateFromTpl(AssemblyInfoTpl, filePath);
			return this;
		}

		protected CreatioPackage CreatePackageDirectories() {
			foreach (var directory in _pkgDirectories) {
				var dInfo = Directory.CreateDirectory(Path.Combine(FullPath, directory));
				AddPlaceholderFile(dInfo.FullName);
			}
			return this;
		}

		protected CreatioPackage CreatePackageFiles() {
			CreatePkgDescriptor()
				.CreateProj()
				.CreateSolution()
				.CreatePackageConfig()
				.CreateAssemblyInfo()
				.CreateEmptyClass();
			return this;
		}

		public static CreatioPackage CreatePackage(string name, string maintainer) {
			return new CreatioPackage(name, maintainer) {
				ProjectId = Guid.NewGuid(),
			};
		}

		public void Create() {
			CreatePackageDirectories().CreatePackageFiles();
		}

		internal void RemovePackageConfig() {
			var filePath = Path.Combine(FullPath, PackageConfigName);
			File.Delete(filePath);
		}
	}
}
