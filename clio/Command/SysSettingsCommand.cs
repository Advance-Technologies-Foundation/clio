using System;
using System.Globalization;
using System.IO;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;
using CreatioModel;
using Newtonsoft.Json.Linq;

namespace Clio.Command
{
	[Verb("set-syssetting", Aliases = new string[] { "syssetting", "sys-setting" }, HelpText = "Set setting value")]
	public class SysSettingsOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Code", Required = true, HelpText = "Syssetting code")]
		public string Code { get; set; }

		[Value(1, MetaName = "Value", Required = true, HelpText = "Syssetting Value")]
		public string Value { get; set; }

		[Value(2, MetaName = "Type", Required = false, HelpText = "Type", Default = "Boolean")]
		public string Type { get; set; }
		
		[Option("GET", Required = false, HelpText = "", Default = "Boolean")]
		public bool IsGet { get; set; }

	}

	public class SysSettingsCommand : RemoteCommand<SysSettingsOptions>
	{
		private readonly IDataProvider _dataProvider;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly IFileSystem _filesystem;
		private readonly ISysSettingsManager _sysSettingsManager;

		public SysSettingsCommand(IApplicationClient applicationClient, EnvironmentSettings settings, 
			IDataProvider dataProvider, IWorkingDirectoriesProvider workingDirectoriesProvider, 
			IFileSystem filesystem, ISysSettingsManager sysSettingsManager)
			: base(applicationClient, settings){
			_dataProvider = dataProvider;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_filesystem = filesystem;
			_sysSettingsManager = sysSettingsManager;
		}

		private string InsertSysSettingsUrl => RootPath + @"/DataService/json/SyncReply/InsertSysSettingRequest";
		private string PostSysSettingsValuesUrl => RootPath + @"/DataService/json/SyncReply/PostSysSettingsValues";
		private string SelectQueryUrl => RootPath + @"/DataService/json/SyncReply/SelectQuery";

		private void CreateSysSetting(SysSettingsOptions opts) {
			Guid id = Guid.NewGuid();
			string requestData = "{" + string.Format("\"id\":\"{0}\",\"name\":\"{1}\",\"code\":\"{1}\",\"valueTypeName\":\"{2}\",\"isCacheable\":true",
				id, opts.Code, opts.Type) + "}";
			try {
				ApplicationClient.ExecutePostRequest(InsertSysSettingsUrl, requestData);
				Logger.WriteInfo($"SysSettings with code: {opts.Code} created.");
			} catch {
				Logger.WriteError($"SysSettings with code: {opts.Code} already exists.");
			}
		}

		
		public void UpdateSysSetting(SysSettingsOptions opts, EnvironmentSettings settings = null) {
			string requestData = string.Empty;
			
			var sysSetting = GetSysSettingType(opts.Code);
			string optionsType = opts.Type ?? sysSetting.ValueTypeName;
			
			
			if (optionsType.Contains("Text") || optionsType.Contains("Date") || optionsType.Contains("Lookup"))
			{
				if(optionsType == "Lookup") {
					
					bool isGuid = Guid.TryParse(opts.Value, out Guid id);
					if(!isGuid) {
						Guid referenceSchemaUIduid = sysSetting.ReferenceSchemaUIdId;
						string entityName = GetSysSchemaNameByUid(referenceSchemaUIduid);
						var entityid = GetEntityIdByDisplayValue(entityName, opts.Value);
						opts.Value = entityid.ToString();
					}
				}
				
				
				
				if(optionsType.Contains("Date")) {
					opts.Value = DateTime.Parse(opts.Value, CultureInfo.InvariantCulture).ToString("yyyy-MM-ddTHH:mm:ss");
				}
				
				//Enclosed opts.Value in "", otherwise update fails for all text settings
				requestData = "{\"isPersonal\":false,\"sysSettingsValues\":{" + string.Format("\"{0}\":\"{1}\"", opts.Code, opts.Value) + "}}";
			}
			else
			{
				requestData = "{\"isPersonal\":false,\"sysSettingsValues\":{" + string.Format("\"{0}\":{1}", opts.Code, opts.Value) + "}}";
			}
			ApplicationClient.ExecutePostRequest(PostSysSettingsValuesUrl, requestData);
			Logger.WriteInfo($"SysSettings with code: {opts.Code} updated.");
		}

		private Guid GetEntityIdByDisplayValue(string entityName, string optsValue){
			
			string jsonFilePath = Path.Join(
				_workingDirectoriesProvider.TemplateDirectory, "dataservice-requests","selectIdByDisplayValue.json");
			
			string jsonContent = _filesystem.ReadAllText(jsonFilePath);
			jsonContent = jsonContent.Replace("{{rootSchemaName}}", entityName);
			jsonContent = jsonContent.Replace("{{diplayvalue}}",optsValue);
			
			
			string responseJson = ApplicationClient.ExecutePostRequest(SelectQueryUrl, jsonContent);
			JObject json = JObject.Parse(responseJson);
			string jsonPath = "$.rows[0].Id";
			string id = (string)json.SelectToken(jsonPath);
			
			bool isGuid = Guid.TryParse(id, out Guid value);
			
			if(isGuid) {
				return value;
			}else {
				return Guid.Empty;
			}
			
		}

		
		
		
		private VwSysSetting GetSysSettingType(string code){
			var sysSetting = AppDataContextFactory.GetAppDataContext(_dataProvider)
            			.Models<VwSysSetting>()
						.Where(i=> i.Code == code)
            			.ToList().FirstOrDefault();
			return sysSetting;
		}
		private string GetSysSchemaNameByUid(Guid uid){
			var sysSchema = AppDataContextFactory.GetAppDataContext(_dataProvider)
            			.Models<SysSchema>()
						.Where(i=> i.UId == uid)
            			.ToList().FirstOrDefault();
			return sysSchema.Name;
		}

		public void TryUpdateSysSetting(SysSettingsOptions opts, EnvironmentSettings settings = null) {
			try {
				UpdateSysSetting(opts, settings);
			} catch {
				Logger.WriteError($"SysSettings with code: {opts.Code} is not updated.");
			}
		}

		public override int Execute(SysSettingsOptions opts) {
			
			
			if(opts.IsGet) {
				var value = _sysSettingsManager.GetSysSettingValueByCode(opts.Code);
				Logger.WriteInfo($"SysSetting {opts.Code} : {value}");
				return 0;
			}
			
			try {
				CreateSysSetting(opts);
				UpdateSysSetting(opts);
			} catch (Exception ex) {
				Logger.WriteError($"Error during set setting '{opts.Code}' value occured with message: {ex.Message}");
				return 1;
			}
			return 0;
		}

	}
	
	
    
}
