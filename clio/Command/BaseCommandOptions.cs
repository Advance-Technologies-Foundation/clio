using CommandLine;

namespace Clio
{
	public class BaseCommandOptions
	{

		[Option("fail-on-error", Required = false, HelpText = "Return fail code on errors")]
		public bool FailOnError {
			get {
				return GlobalContext.FailOnError;
			}
			set {
				GlobalContext.FailOnError = value;
			}
		}

		// The long name used to be declared as "--fail-on-error", which the parser accepts only as
		// "----fail-on-error"; kept hidden so scripts written against that spelling keep working.
		[Option("--fail-on-error", Required = false, Hidden = true, HelpText = "Alias for --fail-on-error")]
		public bool FailOnErrorAlias {
			get => FailOnError;
			set { if (value) FailOnError = value; }
		}

		[Option("fail-on-warning", Required = false, HelpText = "Return fail code on warnings ")]
		public bool FailOnWarning {
			get {
				return GlobalContext.FailOnWarning;
			}
			set {
				GlobalContext.FailOnWarning = value;
			}
		}

		// Same legacy "----fail-on-warning" spelling as FailOnErrorAlias.
		[Option("--fail-on-warning", Required = false, Hidden = true, HelpText = "Alias for --fail-on-warning")]
		public bool FailOnWarningAlias {
			get => FailOnWarning;
			set { if (value) FailOnWarning = value; }
		}
	}
}