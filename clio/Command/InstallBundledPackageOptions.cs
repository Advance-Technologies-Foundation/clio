using CommandLine;

namespace Clio.Command;

/// <summary>
/// Command-line options shared by every verb that installs or updates a bundled package.
/// </summary>
/// <remarks>
/// Every derived options class deliberately carries neither <c>[RequiresPackage]</c> nor <c>[FeatureToggle]</c>.
/// <list type="bullet">
/// <item><description>
/// A <c>[RequiresPackage]</c> here would be self-defeating: both dispatch chokepoints enforce package
/// requirements BEFORE the command runs, so the installer would be refused by the very requirement it
/// exists to satisfy.
/// </description></item>
/// <item><description>
/// A <c>[FeatureToggle]</c> would make the remediation unreachable. A gated options type is filtered out
/// of the verb parse array, so the verb becomes indistinguishable from a typo — and the refusals that
/// name the verb keep pointing users at it.
/// </description></item>
/// </list>
/// </remarks>
public abstract class InstallBundledPackageOptions : EnvironmentNameOptions {

	/// <summary>
	/// Installs despite either backwards-move refusal: an environment already carrying a NEWER version than
	/// this clio ships, or a bundled version stamped with a pre-release suffix.
	/// </summary>
	/// <remarks>
	/// Deliberately CLI-only — it is not exposed on the MCP tool. Rolling a package back is a decision with
	/// consequences for everyone else on that environment, and an agent working a user's business task is
	/// not the right party to take it. Both refusals name this flag so a human can.
	/// <para>
	/// <c>new</c> on purpose: <see cref="EnvironmentOptions"/> already declares a <c>--force</c> ("Force
	/// restore") that is inherited here and means nothing for this verb. Shadowing it gives the flag this
	/// verb's own help text. The compiler will NOT point the shadowing out — <c>CS0108</c> is in the
	/// project's <c>NoWarn</c> list — hence this note. Keep the type <c>bool</c>: CommandLineParser and
	/// <c>CommandHelpRenderer</c> both de-duplicate by name+signature, so a differing signature would make
	/// <c>--force</c> appear TWICE in help, once with each description, with nothing to flag it.
	/// </para>
	/// </remarks>
	[Option("force", Required = false,
		HelpText = "Install even if it would downgrade the package in the environment, or if this clio's "
			+ "bundled version carries a pre-release suffix")]
	public new bool Force { get; set; }

}
