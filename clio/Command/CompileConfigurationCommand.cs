using System;
using System.Threading;
using Clio.Common;
using CommandLine;

namespace Clio.Command
{

	#region Class: CompileConfigurationOptions

	[Verb("compile-configuration", Aliases = ["compile-remote"], HelpText = "Compile configuration for selected environment")]
	public class CompileConfigurationOptions : RemoteCommandOptions
	{

		[Option("all", Required = false, HelpText = "Compile configuration all", Default = false)]
		public bool All {
			get; set;
		}
		protected override int DefaultTimeout => Timeout.Infinite;
		
	}

	#endregion

	#region Interface: CompileConfigurationCommand

	public interface ICompileConfigurationCommand {
		int Execute(CompileConfigurationOptions options);

	}

	#endregion

	#region Class: CompileConfigurationCommand
	
	public class CompileConfigurationCommand : RemoteCommand<CompileConfigurationOptions>, ICompileConfigurationCommand {
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		
		private bool _compileAll;

		#region Constructors: Public

		public CompileConfigurationCommand(IApplicationClient applicationClient, EnvironmentSettings settings, IServiceUrlBuilder serviceUrlBuilder)
			: base(applicationClient, settings)
		{
			_serviceUrlBuilder = serviceUrlBuilder;
		}

		#endregion

		protected override string ServicePath => _compileAll 
			? _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.CompileAll) 
			: _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Compile);


		public override int Execute(CompileConfigurationOptions options) {
			_compileAll = options.All;
			return base.Execute(options);
		}

	}

	#endregion

}
