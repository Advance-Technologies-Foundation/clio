using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;
using CreatioModel;
using YamlDotNet.Serialization;

namespace Clio.Command;

[Verb("save-state", Aliases = new[] {"state", "save-manifest"}, HelpText = "Save state of Creatio instance to file")]
internal class SaveSettingsToManifestOptions : EnvironmentNameOptions
{

	#region Properties: Public

	[Value(0, MetaName = "ManifestName", Required = true, HelpText = "Path to Manifest file")]
	public string ManifestFileName { get; internal set; }

	[Option("overwrite", Required = false,
			HelpText = "Overwrite manifest file if exists", Default = true)]
	public bool Overwrite { get; internal set; }

	public bool SkipDone { get; set; }

	#endregion

}


internal class SaveSettingsToManifestCommand : BaseDataContextCommand<SaveSettingsToManifestOptions>
{

	#region Fields: Private

	private readonly IFileSystem _fileSystem;
	private readonly ISerializer _yamlSerializer;
	private readonly IWebServiceManager _webServiceManager;
	private readonly IEnvironmentManager _environmentManager;
	private readonly ISysSettingsManager _sysSettingsManager;

	#endregion

	#region Constructors: Public

	public SaveSettingsToManifestCommand(IDataProvider provider, ILogger logger, IFileSystem fileSystem,
		ISerializer yamlSerializer, IWebServiceManager webServiceManager, IEnvironmentManager environmentManager, ISysSettingsManager sysSettingsManager)
		: base(provider, logger){
		_fileSystem = fileSystem;
		_yamlSerializer = yamlSerializer;
		_webServiceManager = webServiceManager;
		this._environmentManager = environmentManager;
		this._sysSettingsManager = sysSettingsManager;
	}

	public SaveSettingsToManifestCommand(IDataProvider provider, ILogger logger, IFileSystem fileSystem,
	ISerializer yamlSerializer, IWebServiceManager webServiceManager, IEnvironmentManager environmentManager)
	: base(provider, logger) {
		_fileSystem = fileSystem;
		_yamlSerializer = yamlSerializer;
		_webServiceManager = webServiceManager;
		this._environmentManager = environmentManager;
	}

	#endregion

	#region Methods: Public

	public override int Execute(SaveSettingsToManifestOptions options){
		_logger.WriteInfo($"Operating on environment: {options.Uri}");
		_logger.WriteInfo("Loading information about webservices");
		List<CreatioManifestWebService> services = _webServiceManager?.GetCreatioManifestWebServices();
		_logger.WriteInfo("Loading features");
		List<Feature> features = GetFeatureValues();
		_logger.WriteInfo("Loading packages");
		List<CreatioManifestPackage> packages = GetPackages();
		_logger.WriteInfo("Loading sys settings");
		List<CreatioManifestSetting> settings = GetSysSettingsValue();
		EnvironmentManifest environmentManifest = new() {
			WebServices = services,
			Features = features,
			Packages = packages,
			Settings = settings
		};
		if (options.Uri != null) {
			environmentManifest.EnvironmentSettings = new EnvironmentSettings() { Uri = options.Uri };
		}
		_logger.WriteInfo($"Saving file {options.ManifestFileName}");
		_environmentManager.SaveManifestToFile(options.ManifestFileName, environmentManifest, options.Overwrite);
		
		if(!options.SkipDone) {
			_logger.WriteInfo("Done");
		}
		return 0;
	}

	private List<CreatioManifestSetting> GetSysSettingsValue() {
		if (_sysSettingsManager != null) {
			List<SysSettings> settings = _sysSettingsManager.GetAllSysSettingsWithValues();
			List<CreatioManifestSetting> result = new();
			foreach (var setting in settings) {
				var s = new CreatioManifestSetting() {
					Code = setting.Code,
					Value = setting.DefValue
				};
				if(s.HasValue()) {
					result.Add(s);
				}
			}
			return result.OrderBy(s => s.Code).ToList();
		} else {
			return null;
		}
	}

	private List<CreatioManifestPackage> GetPackages() {
		List<CreatioManifestPackage> packages = new List<CreatioManifestPackage>();
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_provider);
		List<SysPackage> sysPackages = ctx.Models<SysPackage>().ToList();
        foreach (var sysPackage in sysPackages.OrderBy(p => p.Name)) {
			var manifestPackages = new CreatioManifestPackage() {
				Name = sysPackage.Name,
				Hash = GetSysPackageHash(sysPackage)
			};
			packages.Add(manifestPackages);
        }
        return packages;
	}

	private string GetSysPackageHash(SysPackage sysPackage) {
		string dateTimeFormat = "M/dd/yyyy hh:mm:ss tt";
		StringBuilder sb = new StringBuilder();
		sb.Append(sysPackage.Name);
		sb.Append(sysPackage.ModifiedOn.ToString(dateTimeFormat, CultureInfo.InvariantCulture).ToUpper());
		var unOrderList = sysPackage.SysSchemas.ToList();

		foreach (var schema in unOrderList.OrderBy(schema => schema.UId)) {
			sb.Append(schema.UId);
			sb.Append(schema.Checksum);
			sb.Append(schema.ModifiedOn.ToString(dateTimeFormat, CultureInfo.InvariantCulture).ToUpper());
        }
		string hashSource = sb.ToString();
		byte[] bytes = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(hashSource));
		string md5Hash = BitConverter.ToString(bytes).Replace("-", string.Empty);
		return md5Hash;
	}

	private List<Feature> GetFeatureValues(){
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_provider);
		List<Feature> resultList = new();
		List<AppFeature> features = ctx.Models<AppFeature>().ToList();
		int count = 0;
		foreach(var feature in features) {
			count++;
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
			_logger.Write($"Loaded {count} out of {features.Count} features.\r ");
		}
		_logger.WriteLine(string.Empty);
		return resultList;
	}
	
	
	#endregion

}

