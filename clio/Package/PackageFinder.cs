using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio
{
	public class PackageFinder : IPackageFinder
	{
		private PackageInfo TryGetPackageInfo(string potentialPackagePath) {
			string packageDescriptorPath = Path.Combine(potentialPackagePath, CreatioPackage.DescriptorName);
			if (!File.Exists(packageDescriptorPath)) {
				return null;
			}
			try {
				string json = File.ReadAllText(packageDescriptorPath);
				JObject descriptorJson = JsonConvert.DeserializeObject<JObject>(json);
				JToken descriptor = descriptorJson["Descriptor"];
				string name = (string)descriptor["Name"];
				string maintainer = (string)descriptor["Maintainer"];
				JToken dependsOn = descriptor["DependsOn"];
				IEnumerable<string> depends = dependsOn.Select(dependOn => (string)dependOn["Name"]);
				IEnumerable<string> filePaths = Directory
					.EnumerateFiles(potentialPackagePath, "*.*", SearchOption.AllDirectories);
				return new PackageInfo(name, filePaths, depends, maintainer);
			}
			catch (Exception) {
				return null;
			}
		}

		public IDictionary<string, PackageInfo> Find(string packagesPath) {
			if (string.IsNullOrWhiteSpace(packagesPath)) {
				throw new ArgumentNullException(nameof(packagesPath));
			}
			if (!Directory.Exists(packagesPath)) {
				throw new ArgumentException($"Invalid packages path: '{packagesPath}'");
			}
			return Directory.EnumerateDirectories(packagesPath)
				.Select(TryGetPackageInfo)
				.Where(packageInfo => packageInfo != null)
				.ToDictionary(packageInfo => packageInfo.Name, packageInfo => packageInfo);
		}
	}
}