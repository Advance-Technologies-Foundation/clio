using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace bpmcli
{
	public class BpmPkg
	{

		public const string DesriptorName = "descriptor.json";
		public const string PropertiesDirName = "Properties";
		public const string CsprojExtension = "csproj";
		public const string PackageConfigName = "packages.config";
		public const string AssemblyInfoName = "AssemblyInfo.cs";

		private readonly string[] _pkgDirectories = {"Assemblies", "Data", "Schemas", "SqlScripts", "Resources" };

		private static string DescriptorTpl => $"tpl\\{DesriptorName}.tpl";
		private static string ProjTpl => $"tpl\\Proj.{CsprojExtension}.tpl";
		private static string PackageConfigTpl => $"tpl\\{PackageConfigName}.tpl";
		private static string AssemblyInfoTpl => $"tpl\\{AssemblyInfoName}.tpl";

		public string PackageName { get; }

		public string Maintainer { get; }

		public Guid ProjectId { get; protected set; }

		public string Directory { get; protected set; }

		private DateTime _createdOn;
		public DateTime CreatedOn {
			get => _createdOn;
			protected set => _createdOn = GetDateTimeTillSeconds(value);
		}

		protected BpmPkg(string packageName, string maintainer) {
			PackageName = packageName;
			Maintainer = maintainer;
			CreatedOn = DateTime.UtcNow;
			Directory = Environment.CurrentDirectory;
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

		private string GetPathFromEnvironment() {
			string[] cliPath = (Environment.GetEnvironmentVariable("PATH")?.Split(';'));
			return cliPath?.First(p => p.Contains("bpmcli"));
		}

		private bool GetTplPath(string tplPath, out string fullPath) {
			if (File.Exists(tplPath)) {
				fullPath = tplPath;
				return true;
			}
			var envPath = GetPathFromEnvironment();
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

		protected BpmPkg CreatePkgDescriptor() {
			var filePath = Path.Combine(Directory, DesriptorName);
			CreateFromTpl(DescriptorTpl, filePath);
			return this;
		}

		protected BpmPkg CreateProj() {
			var filePath = Path.Combine(Directory, PackageName + "." + CsprojExtension);
			CreateFromTpl(ProjTpl, filePath);
			return this;
		}

		protected BpmPkg CreatePackageConfig() {
			var filePath = Path.Combine(Directory, PackageConfigName);
			CreateFromTpl(PackageConfigTpl, filePath);
			return this;
		}

		protected BpmPkg CreateAssemblyInfo() {
			System.IO.Directory.CreateDirectory(Path.Combine(Directory, PropertiesDirName));
			var filePath = Path.Combine(Directory, PropertiesDirName, AssemblyInfoName);
			CreateFromTpl(AssemblyInfoTpl, filePath);
			return this;
		}

		protected BpmPkg CreateEmptyClass() {
			System.IO.Directory.CreateDirectory(Path.Combine(Directory, "Files\\cs"));
			File.CreateText(Path.Combine(Directory, "Files\\cs", "EmptyClass.cs")).Dispose();
			return this;
		}

		protected BpmPkg CreatePackageDirectories() {
			foreach (var directory in _pkgDirectories) {
				System.IO.Directory.CreateDirectory(Path.Combine(Directory, directory));
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
