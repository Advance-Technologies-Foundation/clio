using Clio.Common;
using CommandLine;
using System;
using System.Reflection;

namespace Clio.Command
{
	[Verb("info", Aliases = new string[] { "ver","get-version","i" }, HelpText = "Check for Creatio packages updates in NuGet")]
	public class InfoCommandOptions
	{
		[Option("all", Required = false, HelpText = "Get versions for all known components")]
		public bool All
		{
			get; set;
		}
		
		[Option('s', "settings-file",  Required = false, HelpText = "Get path to settings file")]
		public bool ShowSettingsFilePath
		{
			get; set;
		}

		[Option("clio", Required = false, HelpText = "Get clio version")]
		public bool Clio
		{
			get; set;
		}

		[Option("gate", Required = false, HelpText = "Get clio-gate version")]
		public bool Gate
		{
			get; set;
		}

		[Option("runtime", Required = false, HelpText = "Get dotnet version")]
		public bool Runtime
		{
			get; set;
		}
	}

	public class InfoCommand : Command<InfoCommandOptions>
	{
		private const string _gateVersion = "2.0.0.29";
		private readonly ILogger _logger;

		public InfoCommand(ILogger logger)
        {
			_logger = logger;
		}

        public override int Execute(InfoCommandOptions options)
		{
			if (options is object && options.Clio)
			{
				_logger.WriteInfo($"clio:   {Assembly.GetEntryAssembly().GetName().Version}");
				return 0;
			}
			else if (options is object && options.Runtime)
			{
				_logger.WriteInfo($"dotnet: {Environment.Version.ToString()}");
				return 0;
			}
			else if (options is object && options.Gate)
			{
				_logger.WriteInfo($"gate:   {_gateVersion}");
				return 0;
			}
			else if(options.ShowSettingsFilePath) {
				_logger.WriteInfo(SettingsRepository.AppSettingsFile);
				return 0;
			}
			else if (options is object && options.All || (!options.Runtime && !options.Gate && !options.Clio && !options.ShowSettingsFilePath))
			{
				_logger.WriteLine($"clio:   {Assembly.GetEntryAssembly().GetName().Version}");
				_logger.WriteInfo($"gate:   {_gateVersion}");
				_logger.WriteLine($"dotnet:   {Environment.Version.ToString()}");
				_logger.WriteInfo($"settings file path: {SettingsRepository.AppSettingsFile}");
				return 0;
			}
			return 1;
		}
	}
}