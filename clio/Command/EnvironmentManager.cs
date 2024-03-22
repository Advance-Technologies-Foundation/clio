using CreatioModel;
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using YamlDotNet.Serialization;

namespace Clio.Command
{
	public class EnvironmentManager:IEnvironmentManager
	{
		private IFileSystem fileSystem;
		private IDeserializer yamlDesirializer;

		public EnvironmentManager(IFileSystem fileSystem, IDeserializer deserializer) {
			this.fileSystem = fileSystem;
			this.yamlDesirializer = deserializer;
		}

		public int ApplyManifest(string manifestFilePath) {
			throw new NotImplementedException();
		}

		public List<SysInstalledApp> FindApllicationsInAppHub(string manifestFilePath) {
			var applications = GetApplicationsFromManifest(manifestFilePath);
			var appHubs = GetAppHubsFromManifest(manifestFilePath);
			List<SysInstalledApp> appsFromAppHub = new List<SysInstalledApp>();
			foreach(var app in applications) {
				foreach(var app_hub in appHubs) {
					if(app_hub.Name == app.AppHubName) {
						app.ZipFileName = app_hub.GetAppZipFileName(app.Name, app.Version);
						appsFromAppHub.Add(app);
					}
				}	
			}
			return appsFromAppHub;
		}



		private IEnumerable<AppHubInfo> GetAppHubsFromManifest(string manifestFilePath) {
			var manifest = fileSystem.File.ReadAllText(manifestFilePath);
			var envManiifest = yamlDesirializer.Deserialize<EnvironmentManifest>(manifest);
			return envManiifest.AppHubs;
		}

		public  List<SysInstalledApp> GetApplicationsFromManifest(string manifestFilePath) {
			var manifest = fileSystem.File.ReadAllText(manifestFilePath);
			var envManiifest = yamlDesirializer.Deserialize<EnvironmentManifest>(manifest);
			return envManiifest.Applications;
		}

		public EnvironmentSettings GetEnvironmentFromManifest(string manifestFilePath) {
			var manifest = fileSystem.File.ReadAllText(manifestFilePath);
			var envManiifest = yamlDesirializer.Deserialize<EnvironmentManifest>(manifest);
			return envManiifest.EnvironmentSettings;
		}
	}

	public interface IEnvironmentManager
	{
		List<SysInstalledApp> GetApplicationsFromManifest(string manifestFilePath);

		int ApplyManifest(string manifestFilePath);

		List<SysInstalledApp> FindApllicationsInAppHub(string manifestFilePath);
		EnvironmentSettings GetEnvironmentFromManifest(string manifestFilePath);
	}
}