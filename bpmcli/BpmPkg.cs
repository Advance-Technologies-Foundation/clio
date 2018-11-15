using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
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


		private readonly Stack<string> _createdFiles = new Stack<string>(5);

		private Stack<string> _createdDirs = new Stack<string>(7);

		private static string DescriptorTpl => $"tpl\\{DesriptorName}.tpl";
		private static string ProjTpl => $"tpl\\Proj.{CsprojExtension}.tpl";
		private static string PackageConfigTpl => $"tpl\\{PackageConfigName}.tpl";
		private static string AssemblyInfoTpl => $"tpl\\{AssemblyInfoName}.tpl";

		public string PackageName { get; }

		public string Maintainer { get; }

		public Guid ProjectId { get; protected set; }

		public string Directory { get; protected set; }

		public DateTime CreatedOn { get; protected set; }

		protected BpmPkg(string packageName, string maintainer) {
			PackageName = packageName;
			Maintainer = maintainer;
			CreatedOn = DateTime.UtcNow;
		}

		private string ReplaceMacro(string text) {
			JsonSerializerSettings microsoftDateFormatSettings = new JsonSerializerSettings {
				DateFormatHandling = DateFormatHandling.MicrosoftDateFormat
			};
			string microsoftJson = JsonConvert.SerializeObject(CreatedOn, microsoftDateFormatSettings);

			return text.Replace("$safeprojectname$", PackageName)
				.Replace("$userdomain$", Maintainer)
				.Replace("$guid1$", ProjectId.ToString())
				.Replace("$year$", CreatedOn.Year.ToString())
				.Replace("$modifiedon$", microsoftJson);
		}

		private bool CreateFromTpl(string tplPath, string filePath) {
			if (File.Exists(tplPath)) {
				var text = ReplaceMacro(File.ReadAllText(tplPath));
				FileInfo file = new FileInfo(filePath);
				using (StreamWriter sw = file.CreateText()) {
					sw.Write(text);
				}
				_createdFiles.Push(file.FullName);
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

		public static BpmPkg CreatePackage(string name) {
			return new BpmPkg(name, "Terrasoft") {
				ProjectId = Guid.NewGuid(),
				Directory = Environment.CurrentDirectory
			};
		}

		public void Create() {
			CreatePkgDescriptor()
				.CreateProj()
				.CreatePackageConfig()
				.CreateAssemblyInfo();
		}


	}
}
