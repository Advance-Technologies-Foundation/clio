using CommandLine;

namespace Clio.Command
{
	[Verb("upload-licenses", Aliases = new string[] { "lic" }, HelpText = "Upload licenses")]
	public class UploadLicensesOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "FilePath", Required = true, HelpText = "License file path")]
		public string FilePath { get; set; }
	}
}
