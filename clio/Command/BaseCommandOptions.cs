using CommandLine;

namespace Clio
{
	/// <summary>
	/// Options shared by the verbs that accept <c>--fail-on-error</c> and <c>--fail-on-warning</c>
	/// (<c>assert</c> and <c>hosts</c>).
	/// </summary>
	public class BaseCommandOptions
	{

		/// <summary>
		/// Gets or sets <see cref="GlobalContext.FailOnError"/>. Accepted for compatibility: only package and
		/// application installs read it, and neither verb that accepts it installs anything on its own.
		/// </summary>
		/// <remarks>
		/// In <c>run-scenario</c> the value still reaches installs: every step's options are activated before the
		/// first step runs, so the last step that sets the flag decides it for every install in the scenario,
		/// earlier steps included. A step that uses the legacy key <c>--fail-on-error</c> binds
		/// <see cref="FailOnErrorAlias"/>, which can only turn the flag on; the key <c>fail-on-error</c> can also
		/// turn it off.
		/// </remarks>
		[Option("fail-on-error", Required = false, HelpText = "Accepted for compatibility; has no effect")]
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

		/// <summary>
		/// Gets or sets whether <c>--fail-on-warning</c> was passed. Accepted for compatibility only: nothing ever
		/// read the value, so it no longer feeds <see cref="GlobalContext"/>.
		/// </summary>
		[Option("fail-on-warning", Required = false, HelpText = "Accepted for compatibility; has no effect")]
		public bool FailOnWarning { get; set; }

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