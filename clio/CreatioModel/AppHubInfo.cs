using YamlDotNet.Serialization;

namespace CreatioModel
{
	public class AppHubInfo
	{
		[YamlMember(Alias = "name")]
		public string Name { get; set; }

		[YamlMember(Alias = "url")]
		public string Url { get; set; }

		[YamlMember(Alias = "path")]
		public string Path { get; set; }
	}
}