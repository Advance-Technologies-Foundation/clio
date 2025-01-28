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

	public class SysSettingsCommand : Command<SysSettingsOptions> {
		private readonly ISysSettingsManager _sysSettingsManager;
		private readonly ILogger _logger;
		private readonly IClioGateway _clioGateway;

		public SysSettingsCommand(ISysSettingsManager sysSettingsManager, ILogger logger, IClioGateway clioGateway){
			_sysSettingsManager = sysSettingsManager;
			_logger = logger;
			_clioGateway = clioGateway;
		}
		
		private void CreateSysSettingIfNotExists(SysSettingsOptions opts) {
			
			// SysSettingsManager.InsertSysSettingResponse result = 
			// 	_sysSettingsManager.InsertSysSetting(opts.Code, opts.Code, opts.Type);
			
			_sysSettingsManager.CreateSysSettingIfNotExists(opts.Code, opts.Code, opts.Type);
			
			// string text = result switch {
			// 	{ Success: true, Id: var id } when id != Guid.Empty => $"SysSettings with code: {opts.Code} created.",
			// 	{ Success: false, Id: var id } when id == Guid.Empty => $"SysSettings with code: {opts.Code} already exists."
			// };
			//  _logger.WriteInfo(text);
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
				const string minClioGateVersion = "2.0.0.0";
				
				if(!_clioGateway.IsCompatibleWith(minClioGateVersion)) {
					_logger.WriteError($"To view SysSetting value by code requires cliogate package version {minClioGateVersion} or higher installed in Creatio.");

					_logger.WriteInfo(string.IsNullOrWhiteSpace(opts.Environment)
						?  "To install cliogate use the following command: clio install-gate"
						: $"To install cliogate use the following command: clio install-gate -e {opts.Environment}");
					return 0;
				}
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
