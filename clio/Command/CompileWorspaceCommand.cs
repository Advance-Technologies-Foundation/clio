using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("build-workspace", Aliases = new[] { "build", "compile", "compile-all", "rebuild" }, HelpText = "Build/Rebuild worksapce for selected environment")]
	public class CompileOptions : EnvironmentNameOptions
	{
		[Value(0, MetaName = "All", Required = false, HelpText = "Compile all")]
		public bool All { get; set; } = true;
	}


	public class CompileWorkspaceCommand : RemoteCommand<CompileOptions>
	{
		public CompileWorkspaceCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string ServicePath => @"/rest/CreatioApiGateway/CompileWorkspace";

	}

}
