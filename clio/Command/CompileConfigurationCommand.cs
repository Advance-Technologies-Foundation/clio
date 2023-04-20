using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{

	#region Class: CompileConfigurationOptions

	[Verb("compile-configuration", Aliases = new[] { "compile-remote" }, HelpText = "Compile configuration for selected environment")]
	public class CompileConfigurationOptions : EnvironmentNameOptions
	{

		[Option("all", Required = false, HelpText = "Compile configuration all", Default = false)]
		public bool All {
			get; set;
		}

	}

	#endregion

	#region Class: CompileConfigurationCommand
	
	public class CompileConfigurationCommand : RemoteCommand<CompileConfigurationOptions> {
		
		#region Constants: Private

		private static string CompileUrl = @"/ServiceModel/WorkspaceExplorerService.svc/Build";
		private static string CompileAllUrl = @"/ServiceModel/WorkspaceExplorerService.svc/Rebuild";

		#endregion

		private bool _compileAll;

		#region Constructors: Public

		public CompileConfigurationCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		#endregion

		protected override string ServicePath => _compileAll ? CompileAllUrl : CompileUrl;


		public override int Execute(CompileConfigurationOptions options) {
			_compileAll = options.All;
			return base.Execute(options);
		}

	}

	#endregion

}
