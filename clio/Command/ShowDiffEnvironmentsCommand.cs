using ATF.Repository.Providers;
using ATF.Repository;
using Clio.Common;
using CommandLine;
using CreatioModel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Autofac;
using Clio.UserEnvironment;
using DocumentFormat.OpenXml.Drawing;
using k8s.Models;
using YamlDotNet.Serialization;

namespace Clio.Command
{
	[Verb("show-diff", Aliases = new[] {"diff", "compare"},
		HelpText = "Show difference in settings for two Creatio intances")]
	internal class ShowDiffEnvironmentsOptions : EnvironmentNameOptions
	{

		[Option("source", Required = true, HelpText = "Source environment name")]
		public string Source { get; internal set; }

		[Option("target", Required = true, HelpText = "Target environment name")]
		public string Target { get; internal set; }

		[Option("file", Required = false, HelpText = "Diff file name")]
		public string FileName { get; internal set; }

		[Option("overwrite", Required = false, HelpText = "Overwrite existing file", Default = true)]
		public bool Overwrite { get; internal set; }

	}

	internal class ShowDiffEnvironmentsCommand : BaseDataContextCommand<ShowDiffEnvironmentsOptions>
	{
		private readonly IEnvironmentManager _environmentManager;
		private readonly IWorkingDirectoriesProvider _workingDirectoriesProvider;
		private readonly ISerializer _serializer;
		private readonly ISettingsRepository _settingsRepository;

		public ShowDiffEnvironmentsCommand(IEnvironmentManager environmentManager, IDataProvider provider,
			ILogger logger, IWorkingDirectoriesProvider workingDirectoriesProvider,
			ISerializer serializer, ISettingsRepository settingsRepository)
			: base(provider, logger){
			_environmentManager = environmentManager;
			_workingDirectoriesProvider = workingDirectoriesProvider;
			_serializer = serializer;
			_settingsRepository = settingsRepository;
		}

		public override int Execute(ShowDiffEnvironmentsOptions options){
			if(options.Target == options.Source) {
				_logger.WriteInfo("No differences found.");
				return 0;
			}
			var manifestFileName = options.FileName ?? $"diff-{options.Source}-{options.Target}.yaml";
			string sourceName = $"source-{options.Source}-manifest.yaml";
			string targetName = $"target-{options.Target}-manifest.yaml";
			
			_workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
				
				var sourceFilePath = System.IO.Path.Combine(tempDirectory, sourceName);
				var targetFilePath = System.IO.Path.Combine(tempDirectory, targetName);
				
				SaveEnvironmentManifest(options.Source, sourceFilePath);
				SaveEnvironmentManifest(options.Target, targetFilePath);
				
				EnvironmentManifest sourceManifest = _environmentManager.LoadEnvironmentManifestFromFile(sourceFilePath);
				EnvironmentManifest targetManifest = _environmentManager.LoadEnvironmentManifestFromFile(targetFilePath);
				EnvironmentManifest diffManifest = _environmentManager.GetDiffManifest(sourceManifest, targetManifest);
				
				if(string.IsNullOrEmpty(options.FileName) ){
					_logger.WriteInfo("Result diff manifest:");
					var result = _serializer.Serialize(diffManifest);
					if(string.IsNullOrEmpty(result) || result.Trim() == "{}") {
						_logger.WriteInfo("No differences found.");	
					}else {
						_logger.WriteInfo(_serializer.Serialize(diffManifest));
					}
				}
				else{
					_logger.WriteInfo($"Diff manifest saved to {manifestFileName}");
					_environmentManager.SaveManifestToFile(manifestFileName, diffManifest, options.Overwrite);
				}
			});
			_logger.WriteInfo("Done");
			return 0;
		}

		private void SaveEnvironmentManifest(string environmentName, string manifestFilePath){
			_logger.WriteInfo($"Loading environments manifest from {environmentName}");
			var sourceEnv = _settingsRepository.GetEnvironment(environmentName);
			var container = new BindingsModule().Register(sourceEnv);
			var command = container.Resolve<SaveSettingsToManifestCommand>();
			command.Execute(new SaveSettingsToManifestOptions() {
				EnvironmentName = environmentName,
				ManifestFileName = manifestFilePath,
				Overwrite = true,
				SkipDone = true
			});
		}

	}
}