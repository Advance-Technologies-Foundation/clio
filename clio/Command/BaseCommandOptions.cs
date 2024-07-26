using CommandLine;

namespace Clio
{
	public class BaseCommandOptions
	{

		[Option("--fail-on-error", Required = false, HelpText = "Return fail code on errors")]
		public bool FailOnError {
			get {
				return GlobalContext.FailOnError;
			}
			set {
				GlobalContext.FailOnError = value;
			}
		}

		[Option("--fail-on-warning", Required = false, HelpText = "Return fail code on warnings ")]
		public bool FailOnWarning {
			get {
				return GlobalContext.FailOnWarning;
			}
			set {
				GlobalContext.FailOnWarning = value;
			}
		}
	}
}