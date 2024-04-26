using System.IO;
using YamlDotNet.Serialization;

namespace Clio.CreatioModel
{
	public class AppHubInfo
	{
		[YamlMember(Alias = "name")]
		public string Name { get; set; }

		[YamlMember(Alias = "url")]
		public string Url { get; set; }

		[YamlMember(Alias = "path")]
		public string RootPath { get; set; }

		internal string GetAppZipFileName(string name, string version) {
			return Path.Combine(RootPath.Replace('/', Path.DirectorySeparatorChar), name, version, $"{name}_{version}.zip");
		}
	}
}