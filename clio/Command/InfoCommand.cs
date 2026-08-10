using Clio.Common;
using Clio.Project.NuGet;
using CommandLine;
using System;
using System.Reflection;

namespace Clio.Command
{
	[Verb("info", Aliases = ["ver","get-version","i"], HelpText = "Check for Creatio packages updates in NuGet")]
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
		private const string _gateVersion = "2.0.0.44";
		private readonly ILogger _logger;
		private readonly IBundledPackageCatalog _bundledPackageCatalog;

		/// <summary>
		/// Initializes a new instance of the <see cref="InfoCommand"/> class.
		/// </summary>
		/// <param name="logger">Logger used for command output.</param>
		/// <param name="bundledPackageCatalog">
		/// Catalog answering what bundled-package version this clio distribution carries.
		/// </param>
		public InfoCommand(ILogger logger, IBundledPackageCatalog bundledPackageCatalog)
        {
			logger.CheckArgumentNull(nameof(logger));
			bundledPackageCatalog.CheckArgumentNull(nameof(bundledPackageCatalog));
			_logger = logger;
			_bundledPackageCatalog = bundledPackageCatalog;
		}

		// Reported from the archive rather than from a constant, so this line describes the bytes an install
		// would actually ship. A distribution that cannot read its own archive says so here instead of
		// printing a number that is no longer backed by anything — that failure is the whole reason a
		// constant was the wrong carrier (spec/adr/adr-bundled-package-version-source-of-truth.md).
		// Rendered through SanitizeVersionForDisplay for the same reason the convergence message is: the catalog
		// is a READER and hands over whatever the archive's descriptor says, suffix included, and PackageVersion
		// re-emits that suffix verbatim with newlines intact. Only clio's own artifact reaches here, so this is
		// tidiness rather than a defence — but it is a line printed to a console, and a version that could
		// forge extra lines in `clio info` output is worth not having.
		private string GetBundledProcessBuilderVersion() =>
			_bundledPackageCatalog.TryGetVersion(
				BundledPackages.ProcessBuilderPackageName,
				out PackageVersion version,
				out string diagnosis)
				? TextUtilities.SanitizeVersionForDisplay(version)
				: $"unavailable — {diagnosis}";

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
				_logger.WriteInfo($"clio:   {Assembly.GetEntryAssembly().GetName().Version}");
				_logger.WriteInfo($"gate:   {_gateVersion}");
				// The bundled process-builder version, so "what does this clio carry" is answerable without
				// unpacking the archive. Compare it against `clio list-packages -e <env>` to tell whether an
				// environment is behind — and it is the same value the convergence rule compares, because
				// both read it from the archive.
				_logger.WriteInfo($"process-builder:   {GetBundledProcessBuilderVersion()}");
				_logger.WriteInfo($"dotnet:   {Environment.Version.ToString()}");
				_logger.WriteInfo($"settings file path: {SettingsRepository.AppSettingsFile}");
				return 0;
			}
			return 1;
		}
	}
}
