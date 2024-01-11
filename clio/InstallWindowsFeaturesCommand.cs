using Clio.Command;
using CommandLine;

namespace Clio
{
	[Verb("install-windows-features", Aliases = new string[] { "iwf", "inst-win-features"}, HelpText = "Install windows features required for Creatio")]

	internal class InstallWindowsFeaturesOptions
	{
	}

	internal class InstallWindowsFeaturesCommand : Command<InstallWindowsFeaturesOptions>
	{
		public override int Execute(InstallWindowsFeaturesOptions options) {
			throw new System.NotImplementedException();
		}
	}
}