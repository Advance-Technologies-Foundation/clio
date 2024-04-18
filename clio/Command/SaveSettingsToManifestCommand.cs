using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace Clio.Command
{
	internal class SaveSettingsToManifestCommand : BaseDataContextCommand<SaveSettingsToManifestOptions>
	{
		private readonly IFileSystem _fileSystem;
		private readonly ISerializer _yamlSerializer;

		public SaveSettingsToManifestCommand(IDataProvider provider, ILogger logger, IFileSystem fileSystem, ISerializer yamlSerializer) : base(provider, logger) {
			this._fileSystem = fileSystem;
			this._yamlSerializer = yamlSerializer;
		}

		public override int Execute(SaveSettingsToManifestOptions options) {
			EnvironmentManifest environmentManifest = new() {
				WebServices = new List<CreatioManifestWebService> {
					new CreatioManifestWebService {
						Name= "WebService1",
						Url = "https://preprod.creatio.com/0/ServiceModel/EntityDataService.svc"
					},
					new CreatioManifestWebService {
					  Name = "WebService2",
					  Url = "https://preprod.creatio.com/0/ServiceModel/EntityDataService.svc"
					}
				}
			};
			var manifestContent = _yamlSerializer.Serialize(environmentManifest); 
			_fileSystem.WriteAllTextToFile(options.ManifestFileName, manifestContent);
			return 1;
		}
	}

	[Verb("save-state", Aliases = new string[] { "state" }, HelpText = "Save state of Creatio instance to file")]
internal class SaveSettingsToManifestOptions
{
	public string ManifestFileName { get; internal set; }
}
}
