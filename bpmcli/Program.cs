using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Json;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using bpmcli.environment;
using CommandLine;
using ConsoleTables;
using Newtonsoft.Json;

namespace bpmcli
{

	public class StringParser
	{
		public static IEnumerable<string> ParseArray(string input) {
			return input.Split(',').Select(p => p.Trim()).ToList();
		}
	}

	class Program {

		private static int Main(string[] args)
		{
			var autoupdate = new SettingsRepository().GetAutoupdate();
			if (autoupdate)
			{
				new Thread(CommandHelper.CheckUpdate).Start();
			}
			Parser.Default.Settings.ShowHeader = false;
			return Parser.Default.ParseArguments<ExecuteAssemblyOptions, RestartOptions, ClearRedisOptions, FetchOptions,
					RegAppOptions, AppListOptions, UnregAppOptions, GeneratePkgZipOptions, PushPkgOptions,
					DeletePkgOptions, ReferenceOptions, NewPkgOptions, ConvertOptions, RegisterOptions, PullPkgOptions,
					UpdateCliOptions, ExecuteSqlScriptOptions, InstallGateOptions, EntityModelOptions>(args)
				.MapResult(
					(ExecuteAssemblyOptions opts) => CommandHelper.Execute(opts),
					(RestartOptions opts) => CommandHelper.Restart(opts),
					(ClearRedisOptions opts) => CommandHelper.ClearRedisDb(opts),
					(FetchOptions opts) => CommandHelper.Fetch(opts),
					(RegAppOptions opts) => CommandHelper.ConfigureEnvironment(opts),
					(AppListOptions opts) => CommandHelper.ViewEnvironments(opts),
					(UnregAppOptions opts) => CommandHelper.UnregApplication(opts),
					(GeneratePkgZipOptions opts) => CommandHelper.Compression(opts),
					(PushPkgOptions opts) => CommandHelper.Install(opts),
					(DeletePkgOptions opts) => CommandHelper.Delete(opts),
					(ReferenceOptions opts) => CommandHelper.ReferenceTo(opts),
					(NewPkgOptions opts) => CommandHelper.NewPkg(opts),
					(ConvertOptions opts) => CommandHelper.ConvertPackage(opts),
					(RegisterOptions opts) => CommandHelper.Register(opts),
					(PullPkgOptions opts) => CommandHelper.DownloadZipPackages(opts),
					(UpdateCliOptions opts) => CommandHelper.UpdateCli(),
					(ExecuteSqlScriptOptions opts) => CommandHelper.ExecuteSqlScript(opts),
					(InstallGateOptions opts) => CommandHelper.UpdateGate(opts),
					(EntityModelOptions opts) => CommandHelper.GetModels(opts),
					errs => 1);
		}

	}
}
