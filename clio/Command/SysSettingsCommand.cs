using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command.SysSettingsCommand
{
	[Verb("set-syssetting", Aliases = new string[] { "syssetting", "sys-setting" }, HelpText = "Set setting value")]
	internal class SysSettingsOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Code", Required = true, HelpText = "Syssetting code")]
		public string Code { get; set; }

		[Value(1, MetaName = "Value", Required = true, HelpText = "Syssetting Value")]
		public string Value { get; set; }

		[Value(2, MetaName = "Type", Required = false, HelpText = "Type", Default = "Boolean")]
		public string Type { get; set; }

	}

	class SysSettingsCommand : RemoteCommand<SysSettingsOptions>
	{
		public SysSettingsCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		private string InsertSysSettingsUrl => RootPath + @"/DataService/json/SyncReply/InsertSysSettingRequest";
		private string PostSysSettingsValuesUrl => RootPath + @"/DataService/json/SyncReply/PostSysSettingsValues";

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
			if (opts.Type.Contains("Text"))
			{
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

		public void TryUpdateSysSetting(SysSettingsOptions opts, EnvironmentSettings settings = null) {
			try {
				UpdateSysSetting(opts, settings);
			} catch {
				Logger.WriteError($"SysSettings with code: {opts.Code} is not updated.");
			}
		}

		public override int Execute(SysSettingsOptions opts) {
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
