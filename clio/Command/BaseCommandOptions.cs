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

		/// <summary>
		/// Hidden legacy spelling <c>----fail-on-error</c> of <see cref="FailOnError"/>.
		/// </summary>
		/// <remarks>
		/// The long name used to be declared as <c>"--fail-on-error"</c>, which the parser accepts only as
		/// <c>----fail-on-error</c>; kept hidden so scripts written against that spelling keep working. The setter
		/// only turns the flag on, so an unset alias never clears a flag the main option set.
		/// </remarks>
		[Option("--fail-on-error", Required = false, Hidden = true,
			HelpText = "Legacy ----fail-on-error spelling of --fail-on-error")]
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

		/// <summary>
		/// Hidden legacy spelling <c>----fail-on-warning</c> of <see cref="FailOnWarning"/>; see
		/// <see cref="FailOnErrorAlias"/>.
		/// </summary>
		[Option("--fail-on-warning", Required = false, Hidden = true,
			HelpText = "Legacy ----fail-on-warning spelling of --fail-on-warning")]
		public bool FailOnWarningAlias {
			get => FailOnWarning;
			set { if (value) FailOnWarning = value; }
		}
	}
}