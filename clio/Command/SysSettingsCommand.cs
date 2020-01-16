using System;
using System.Collections.Generic;
using System.Text;
using Creatio.Client;
using CommandLine;

namespace Clio.Command.SysSettingsCommand
{
	[Verb("set-syssetting", Aliases = new string[] { "syssetting" }, HelpText = "Set setting value")]
	internal class SysSettingsOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Code", Required = true, HelpText = "Syssetting code")]
		public string Code { get; set; }

		[Value(1, MetaName = "Value", Required = true, HelpText = "Syssetting Value")]
		public string Value { get; set; }

		[Value(2, MetaName = "Type", Required = false, HelpText = "Type", Default = "Boolean")]
		public string Type { get; set; }

	}

	class SysSettingsCommand: BaseRemoteCommand
	{
		private static string InsertSysSettingsUrl => _appUrl + @"/DataService/json/SyncReply/InsertSysSettingRequest";
		private static string PostSysSettingsValuesUrl => _appUrl + @"/DataService/json/SyncReply/PostSysSettingsValues";

		private static void CreateSysSetting(SysSettingsOptions opts) {
			Guid id = Guid.NewGuid();
			string requestData = "{" + string.Format("\"id\":\"{0}\",\"name\":\"{1}\",\"code\":\"{1}\",\"valueTypeName\":\"{2}\",\"isCacheable\":true",
				id, opts.Code, opts.Type) + "}";
			try {
				CreatioClient.ExecutePostRequest(InsertSysSettingsUrl, requestData);
				Console.WriteLine("SysSettings with code: {0} created.", opts.Code);
			} catch {
				Console.WriteLine("SysSettings with code: {0} already exists.", opts.Code);
			}
		}

		public static void UpdateSysSetting(SysSettingsOptions opts, EnvironmentSettings settings = null) {
			if (settings != null) {
				Configure(settings);
			}
			string requestData = "{\"isPersonal\":false,\"sysSettingsValues\":{" + string.Format("\"{0}\":{1}", opts.Code, opts.Value) + "}}";
			try {
				CreatioClient.ExecutePostRequest(PostSysSettingsValuesUrl, requestData);
				Console.WriteLine("SysSettings with code: {0} updated.", opts.Code);
			} catch {
				Console.WriteLine("SysSettings with code: {0} is not updated.", opts.Code);
			}
		}

		public static int SetSysSettings(SysSettingsOptions opts) {
			try {
				Configure(opts);
				CreateSysSetting(opts);
				UpdateSysSetting(opts);
			} catch (Exception ex) {
				Console.WriteLine($"Error during set setting value occured with message: {ex.Message}");
				return 1;
			}
			return 0;
		}
	}
}
