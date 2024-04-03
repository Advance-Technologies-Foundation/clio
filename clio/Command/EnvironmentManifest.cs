using CreatioModel;
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace Clio.Command
{
	public class EnvironmentManifest
	{
		[YamlMember(Alias = "apps")]
		public List<SysInstalledApp> Applications { get; set; }

		[YamlMember(Alias = "app_hubs")]
		public List<AppHubInfo> AppHubs { get; set; }

		[YamlMember(Alias = "environment")]
		public EnvironmentSettings EnvironmentSettings
		{
			get;
			internal set;
		}
	
		[YamlMember(Alias = "features")]
		public List<Feature> Features
		{
			get;
			internal set;
		}

		[YamlMember(Alias = "settings")]
		public List<CreatioManifestSetting> Settings {
			get;
			internal set;
		}

	}
}