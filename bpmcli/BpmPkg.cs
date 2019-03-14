using System;
using System.IO;
using bpmcli.environment;
using Newtonsoft.Json;

namespace bpmcli
{
	public class BpmPkg
	{

		public const string DescriptorName = "descriptor.json";
		public const string PropertiesDirName = "Properties";
		public const string CsprojExtension = "csproj";
		public const string PackageConfigName = "packages.config";
		public const string AssemblyInfoName = "AssemblyInfo.cs";
		public const string PlaceholderFileName = "placeholder.txt";
		public static string EditProjTpl => $"tpl\\EditProj.{CsprojExtension}.tpl";
		public static string PackageConfigTpl => $"tpl\\{PackageConfigName}.tpl";
		public static string AssemblyInfoTpl => $"tpl\\{AssemblyInfoName}.tpl";

		private readonly string[] _pkgDirectories = {"Assemblies", "Data", "Schemas", "SqlScripts", "Resources" };

		private static string DescriptorTpl => $"tpl\\{DescriptorName}.tpl";
		private static string ProjTpl => $"tpl\\Proj.{CsprojExtension}.tpl";

		private readonly IBpmcliEnvironment _bpmcliEnvironment;


		public string PackageName { get; }

		public string Maintainer { get; }

		public Guid ProjectId { get; protected set; }

		public string FullPath { get; protected set; }

		private DateTime _createdOn;
		public DateTime CreatedOn {
			get => _createdOn;
			protected set => _createdOn = GetDateTimeTillSeconds(value);
		}

		protected BpmPkg(string packageName, string maintainer) {
			_bpmcliEnvironment = new BpmcliEnvironment();
			PackageName = packageName;
			Maintainer = maintainer;
			CreatedOn = DateTime.UtcNow;
			FullPath = Environment.CurrentDirectory;
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
			var envPath = _bpmcliEnvironment.GetRegisteredPath();
			if (!string.IsNullOrEmpty(envPath)) {
				fullPath = Path.Combine(envPath, tplPath);
				return true;
			}
			fullPath = null;
			return false;
		}

		private bool CreateFromTpl(string tplPath, string filePath) {
			if (GetTplPath(tplPath, out string fullTplPath)) {
				var text = ReplaceMacro(File.ReadAllText(fullTplPath));
				FileInfo file = new FileInfo(filePath);
				using (StreamWriter sw = file.CreateText()) {
					sw.Write(text);
				}
				return true;
			}
			return false;
		}

		private void AddPlaceholderFile(string dirPath) {
			var placeholderPath = Path.Combine(dirPath, PlaceholderFileName);
			File.Create(placeholderPath).Dispose();
		}

		protected BpmPkg CreatePkgDescriptor() {
			var filePath = Path.Combine(FullPath, DescriptorName);
			CreateFromTpl(DescriptorTpl, filePath);
			return this;
		}

		protected BpmPkg CreateProj() {
			var filePath = Path.Combine(FullPath, PackageName + "." + CsprojExtension);
			CreateFromTpl(ProjTpl, filePath);
			return this;
		}

		protected BpmPkg CreatePackageConfig() {
			var filePath = Path.Combine(FullPath, PackageConfigName);
			CreateFromTpl(PackageConfigTpl, filePath);
			return this;
		}

		protected BpmPkg CreateAssemblyInfo() {
			System.IO.Directory.CreateDirectory(Path.Combine(FullPath, PropertiesDirName));
			var filePath = Path.Combine(FullPath, PropertiesDirName, AssemblyInfoName);
			CreateFromTpl(AssemblyInfoTpl, filePath);
			return this;
		}

		protected BpmPkg CreateEmptyClass() {
			System.IO.Directory.CreateDirectory(Path.Combine(FullPath, "Files\\cs"));
			File.CreateText(Path.Combine(FullPath, "Files\\cs", "EmptyClass.cs")).Dispose();
			return this;
		}

		protected BpmPkg CreatePackageDirectories() {
			foreach (var directory in _pkgDirectories) {
				var dInfo = System.IO.Directory.CreateDirectory(Path.Combine(FullPath, directory));
				AddPlaceholderFile(dInfo.FullName);
			}
			return this;
		}

		protected BpmPkg CreatePackageFiles() {
			CreatePkgDescriptor()
				.CreateProj()
				.CreatePackageConfig()
				.CreateAssemblyInfo()
				.CreateEmptyClass();
			return this;
		}

		public static BpmPkg CreatePackage(string name, string maintainer) {
			return new BpmPkg(name, maintainer) {
				ProjectId = Guid.NewGuid(),
			};
		}

		public void Create() {
			CreatePackageFiles().CreatePackageDirectories();
		}

	}
}
