using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;
using CreatioModel;
using YamlDotNet.Serialization;

namespace Clio.Command;

[Verb("save-state", Aliases = new[] {"state"}, HelpText = "Save state of Creatio instance to file")]
internal class SaveSettingsToManifestOptions : EnvironmentNameOptions
{

	#region Properties: Public

	[Value(0, MetaName = "ManifestName", Required = true, HelpText = "Path to Manifest file")]
	public string ManifestFileName { get; internal set; }

	#endregion

}


internal class SaveSettingsToManifestCommand : BaseDataContextCommand<SaveSettingsToManifestOptions>
{

	#region Fields: Private

	private readonly IFileSystem _fileSystem;
	private readonly ISerializer _yamlSerializer;
	private readonly IWebServiceManager _webServiceManager;

	#endregion

	#region Constructors: Public

	public SaveSettingsToManifestCommand(IDataProvider provider, ILogger logger, IFileSystem fileSystem,
		ISerializer yamlSerializer, IWebServiceManager webServiceManager)
		: base(provider, logger){
		_fileSystem = fileSystem;
		_yamlSerializer = yamlSerializer;
		_webServiceManager = webServiceManager;
	}

	#endregion

	#region Methods: Public

	public override int Execute(SaveSettingsToManifestOptions options){
		List<CreatioManifestWebService> services = _webServiceManager.GetCreatioManifestWebServices();
		List<Feature> features = GetFeatureValues();
		EnvironmentManifest environmentManifest = new() {
			WebServices = services,
			Features = features
		};
		string manifestContent = _yamlSerializer.Serialize(environmentManifest);
		_fileSystem.WriteAllTextToFile(options.ManifestFileName, manifestContent);
		_logger.WriteInfo("Done");
		return 0;
	}

	private List<Feature> GetFeatureValues(){
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_provider);
		List<Feature> resultList = new();
		List<AppFeature> features = ctx.Models<AppFeature>().ToList();
		
		foreach(var feature in features) {
			var f = new Feature() {
				Code = feature.Code
			};
			f.UserValues = new Dictionary<string, bool>();
			
			var featureStateId = ctx.Models<AdminUnitFeatureState>()
				.Where(f => f.FeatureId == feature.Id).ToList();
			
			
			featureStateId.ForEach(fsi=> {
				var state = ctx.Models<AppFeatureState>()
					.Where(i=> i.Id ==fsi.Id).ToList();
				
				state.ForEach(ff=> {
					var name = ff?.AdminUnit?.Name;
					var value = ff?.FeatureState ?? false;
					if(!string.IsNullOrEmpty(name)) {
						if(f.UserValues.ContainsKey(name)) {
							f.UserValues.Add($"{name}_DOUBLE_{ff.AdminUnitId}", value);
						}else {
							f.UserValues.Add(name, value);
						}
					}
				});
				
			});
			if(f.UserValues.Count == 0 && f.Value == false) {
				
			}else {
				resultList.Add(f);
			}
		}
		return resultList;
	}
	
	
	#endregion

}

