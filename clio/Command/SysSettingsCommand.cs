using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("set-syssetting", Aliases = ["ss", "syssetting", "sys-setting", "get-syssetting"], HelpText = "Set setting value")]
	public class SysSettingsOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Code", Required = true, HelpText = "Sys-setting code")]
		public string Code { get; set; }

		[Value(1, MetaName = "Value", Required = false, HelpText = "Sys-setting Value")]
		public string Value { get; set; }

		[Value(2, MetaName = "Type", Required = false, HelpText = "Type", Default = "Text")]
		public string Type { get; set; }

		[Option("GET", Required = false, HelpText = "Use GET to retrieve sys-setting", Default = false)]
		public bool IsGet { get; set; }

	}

	public class SysSettingsCommand : Command<SysSettingsOptions> {
		private readonly ISysSettingsManager _sysSettingsManager;
		private readonly ILogger _logger;

		public SysSettingsCommand(ISysSettingsManager sysSettingsManager, ILogger logger){
			_sysSettingsManager = sysSettingsManager;
			_logger = logger;
		}

		private void CreateSysSettingIfNotExists(SysSettingsOptions opts) {
			_sysSettingsManager.CreateSysSettingIfNotExists(opts.Code, opts.Code, opts.Type);
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
				string value = _sysSettingsManager.GetSysSettingValueByCode(opts.Code);
				_logger.WriteInfo($"SysSettings {opts.Code} : {value}");
				return 0;
			}

			try {
				CreateSysSettingIfNotExists(opts);
				UpdateSysSetting(opts);
			} catch (Exception ex) {
				_logger.WriteError($"Error during set setting '{opts.Code}' value occured with message: {ex.Message}");
				return 1;
			}
			return 0;
		}
	}
}
