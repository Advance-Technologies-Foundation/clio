using CommandLine;
using System;
using System.Reflection;

namespace Clio.Command
{
	[Verb("ver", Aliases = new string[] { "get-version" }, HelpText = "Check for Creatio packages updates in NuGet")]
	public class GetVersionOptions : EnvironmentOptions
	{
		[Option("all", Required = false, HelpText = "Get versions for all known components")]
		public bool All
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

	public class GetVersionCommand : Command<GetVersionOptions>
	{
		private const string _gateVersion = "2.0.0.15";
		public override int Execute(GetVersionOptions options)
		{
			if (options is object && options.Clio)
			{
				Console.WriteLine("clio:   {0}", Assembly.GetEntryAssembly().GetName().Version);
				return 0;
			}
			else if (options is object && options.Runtime)
			{
				Console.WriteLine("dotnet: {0}", Environment.Version.ToString());
				return 0;
			}
			else if (options is object && options.Gate)
			{
				Console.WriteLine("gate:   {0}", _gateVersion);
				return 0;
			}
			else if (options is object && options.All || (!options.Runtime && !options.Gate && !options.Clio))
			{
				Console.WriteLine("clio:   {0}", Assembly.GetEntryAssembly().GetName().Version);
				Console.WriteLine("gate:   {0}", _gateVersion);
				Console.WriteLine("dotnet: {0}", Environment.Version.ToString());
				return 0;
			}
			return 1;
		}
	}
}