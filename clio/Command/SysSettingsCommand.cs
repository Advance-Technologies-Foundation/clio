using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("set-syssetting", Aliases = new[] { "syssetting", "sys-setting", "get-syssetting"}, HelpText = "Set setting value")]
	public class SysSettingsOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Code", Required = true, HelpText = "Syssetting code")]
		public string Code { get; set; }

		[Value(1, MetaName = "Value", Required = false, HelpText = "Syssetting Value")]
		public string Value { get; set; }

		[Value(2, MetaName = "Type", Required = false, HelpText = "Type", Default = "Text")]
		public string Type { get; set; }
		
		[Option("GET", Required = false, HelpText = "", Default = false)]
		public bool IsGet { get; set; }

	}

	public class SysSettingsCommand : Command<SysSettingsOptions>
	{
		private readonly ISysSettingsManager _sysSettingsManager;
		private readonly ILogger _logger;

		public SysSettingsCommand(ISysSettingsManager sysSettingsManager, ILogger logger){
			_sysSettingsManager = sysSettingsManager;
			_logger = logger;
		}
		
		private void CreateSysSetting(SysSettingsOptions opts) {
			
			SysSettingsManager.InsertSysSettingResponse result = 
				_sysSettingsManager.InsertSysSetting(opts.Code, opts.Code, opts.Type);
			
			string text = result switch {
				{ Success: true, Id: var id } when id != Guid.Empty => $"SysSettings with code: {opts.Code} created.",
				{ Success: false, Id: var id } when id == Guid.Empty => $"SysSettings with code: {opts.Code} already exists."
			};
			 _logger.WriteInfo(text);
		}
		
		public void UpdateSysSetting(SysSettingsOptions opts, EnvironmentSettings settings = null) {
			
			bool isUpdated = _sysSettingsManager.UpdateSysSetting(opts.Code, opts.Value);
			if(isUpdated) {
				_logger.WriteInfo($"SysSettings with code: {opts.Code} updated.");
				
			} else {
				_logger.WriteError($"SysSettings with code: {opts.Code} is not updated.");
			}
		}
		public void TryUpdateSysSetting(SysSettingsOptions opts, EnvironmentSettings settings = null) {
			try {
				UpdateSysSetting(opts, settings);
			} catch {
				_logger.WriteError($"SysSettings with code: {opts.Code} is not updated.");
			}
		}
		public override int Execute(SysSettingsOptions opts) {
			
			if(opts.IsGet) {
				var value = _sysSettingsManager.GetSysSettingValueByCode(opts.Code);
				_logger.WriteInfo($"SysSetting {opts.Code} : {value}");
				return 0;
			}
			
			try {
				CreateSysSetting(opts);
				UpdateSysSetting(opts);
			} catch (Exception ex) {
				_logger.WriteError($"Error during set setting '{opts.Code}' value occured with message: {ex.Message}");
				return 1;
			}
			return 0;
		}
	}
}
