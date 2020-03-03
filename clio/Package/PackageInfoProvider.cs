using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clio.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio
{
	public class PackageInfoProvider : IPackageInfoProvider
	{

		private PackageDependency CreateDependencyInfo(JToken jtokenDependency) {
			string name = (string)jtokenDependency["Name"];
			string packageVersion = (string)jtokenDependency["PackageVersion"] ?? string.Empty;
			return new PackageDependency(name, packageVersion);
		}

		public PackageInfo GetPackageInfo(string packagePath) {
			packagePath.CheckArgumentNullOrWhiteSpace(nameof(packagePath));
			string packageDescriptorPath = Path.Combine(packagePath, CreatioPackage.DescriptorName);
			if (!File.Exists(packageDescriptorPath)) {
				throw new Exception($"Package descriptor not found by path: '{packageDescriptorPath}'"); 
			}
			try {
				string json = File.ReadAllText(packageDescriptorPath);
				JObject descriptorJson = JsonConvert.DeserializeObject<JObject>(json);
				JToken descriptor = descriptorJson["Descriptor"];
				string name = (string)descriptor["Name"];
				string packageVersion = (string)descriptor["PackageVersion"] ?? string.Empty;
				string maintainer = (string)descriptor["Maintainer"];
				string uid = (string)descriptor["UId"];
				JToken dependsOn = descriptor["DependsOn"];
				IEnumerable<PackageDependency> depends = dependsOn.Select(CreateDependencyInfo);
				IEnumerable<string> filePaths = Directory
					.EnumerateFiles(packagePath, "*.*", SearchOption.AllDirectories);
				return new PackageInfo(name, packageVersion, maintainer, uid, packagePath, filePaths, depends);
			}
			catch (Exception ex) {
				throw new Exception($"Package descriptor is wrong: '{ex.Message}'");
			}
		}

	}
}