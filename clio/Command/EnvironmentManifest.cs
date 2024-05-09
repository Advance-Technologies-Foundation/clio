using CreatioModel;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Collections.Generic;
using Clio.CreatioModel;
using YamlDotNet.Serialization;
using System.Linq;

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

		private List<Feature> _features = new();

		[YamlMember(Alias = "features")]
		public List<Feature> Features {
			get {
				return _features;
			}
			set {
				_features = value ?? new();
			}
		}

		private List<CreatioManifestSetting> _settings = new();

		[YamlMember(Alias = "settings")]
		public List<CreatioManifestSetting> Settings {
			get {
				return _settings;
			}
			set {
				_settings = value ?? new();
			}
		}

		private List<CreatioManifestWebService> _webServices = new ();

		[YamlMember(Alias = "webservices")]
		public List<CreatioManifestWebService> WebServices {
			get {
				return _webServices;
			}
			set {
				_webServices = value ?? new () ;
			}
		}


		private List<CreatioManifestPackage> _packages = new();
		[YamlMember(Alias = "packages")]
		public List<CreatioManifestPackage> Packages {
			get {
				return _packages;
			}
			set {
				_packages = value?.OrderBy(p => p.Name).ToList() ?? new();
			}
		}
	}
}