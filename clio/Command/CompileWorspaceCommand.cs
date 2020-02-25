using Clio.Common;
using CommandLine;

namespace Clio.Command
{
	[Verb("build-workspace", Aliases = new[] { "build", "compile", "compile-all", "rebuild" }, HelpText = "Build/Rebuild worksapce for selected environment")]
	public class CompileOptions : EnvironmentNameOptions
	{
	}


	public class CompileWorkspaceCommand : RemoteCommand<CompileOptions>
	{
		public CompileWorkspaceCommand(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings) {
		}

		protected override string ServicePath => @"/rest/CreatioApiGateway/CompileWorkspace";

	}

}
