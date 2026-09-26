using System;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

[Verb("compile-package", Aliases = ["comp-pkg"], HelpText = "Build package command")]
public class CompilePackageOptions : EnvironmentNameOptions
{

	#region Constants: Internal

	/// <summary>Default for <see cref="WaitTimeout"/>, the same 600 s <c>restart --wait-ready</c> uses.</summary>
	internal const int DefaultWaitTimeoutSeconds = 600;

	/// <summary>Upper bound for <see cref="WaitTimeout"/>, the same bound <c>restart --ready-timeout</c> has.</summary>
	internal const int MaxWaitTimeoutSeconds = 3600;

	#endregion

	#region Properties: Public


	[Value(0, MetaName = "PackageName", Required = true, HelpText = "Specified package name")]
	public string PackageName
	{
		get; set;
	}

	public string[] PackageNames => PackageName.Split(',');

	/// <summary>
	/// Blocks until the environment has finished building instead of returning when the build was accepted.
	/// </summary>
	[Option("wait", Required = false, Default = false,
		HelpText = "Block until the environment reports that the build finished (its build result, or compilation activity that has stopped), instead of returning when the build is accepted")]
	public bool Wait { get; set; }

	/// <summary>
	/// Upper bound, in seconds, on how long <see cref="Wait"/> waits for each package build.
	/// </summary>
	/// <remarks>
	/// Initialized here as well as in the attribute: the attribute default applies only when the parser
	/// builds the options, and the MCP tool constructs them directly.
	/// </remarks>
	[Option("wait-timeout", Required = false, Default = DefaultWaitTimeoutSeconds,
		HelpText = "Max seconds to wait for each package build when --wait is set (default: 600, max: 3600)")]
	public int WaitTimeout { get; set; } = DefaultWaitTimeoutSeconds;

	#endregion

}

public class CompilePackageCommand : Command<CompilePackageOptions>
{

	#region Constants: Internal

	/// <summary>
	/// Heavy-operation warning shown on the interactive CLI before a package compilation (ENG-93157).
	/// Paired with the <c>[Y/N]</c> prompt so the user can proceed now or postpone.
	/// </summary>
	internal const string PackageCompilationWarning =
		"WARNING: Compilation is a heavy operation. It rebuilds the package assemblies and forces a " +
		"runtime reload that may disrupt every user currently connected to this environment.";

	#endregion

	#region Fields: Private

	private readonly IPackageBuilder _packageBuilder;
	private readonly ILogger _logger;
	private readonly IInteractiveConsole _interactiveConsole;

	#endregion

	#region Constructors: Public

	public CompilePackageCommand(IPackageBuilder packageBuilder, ILogger logger,
		IInteractiveConsole interactiveConsole) {
		_packageBuilder = packageBuilder;
		_logger = logger;
		_interactiveConsole = interactiveConsole;
	}

	#endregion

	#region Methods: Private

	/// <summary>
	/// Builds the "how to run it later" hint shown when the user postpones the compilation, echoing the
	/// exact <c>clio compile-package</c> invocation that reproduces the request.
	/// </summary>
	private static string BuildPostponeHint(CompilePackageOptions options) {
		string environmentPart = string.IsNullOrWhiteSpace(options.Environment)
			? string.Empty
			: $" -e {options.Environment}";
		string waitPart = options.Wait ? " --wait" : string.Empty;
		return $"Compilation postponed. Nothing was compiled. Run it later with: clio compile-package {options.PackageName}{environmentPart}{waitPart}";
	}

	#endregion

	#region Methods: Public

	public override int Execute(CompilePackageOptions options) {
		if (!_interactiveConsole.ConfirmHeavyOperation(options.IsSilent, PackageCompilationWarning, _logger, BuildPostponeHint(options))) {
			// The user chose to postpone: nothing is compiled. Return the distinct DeclinedExitCode (not 0)
			// so in-process callers and shell chains can tell it apart from a successful build. Only
			// reachable on an interactive, non-silent terminal.
			return InteractiveConsoleExtensions.DeclinedExitCode;
		}
		if (options.Wait && options.WaitTimeout is <= 0 or > CompilePackageOptions.MaxWaitTimeoutSeconds) {
			_logger.WriteError(
				$"--wait-timeout must be between 1 and {CompilePackageOptions.MaxWaitTimeoutSeconds} seconds; got {options.WaitTimeout}.");
			return 1;
		}
		try {
			if (options.Wait) {
				_packageBuilder.Rebuild(options.PackageNames,
					new PackageCompilationWaitOptions(TimeSpan.FromSeconds(options.WaitTimeout)));
			} else {
				_packageBuilder.Rebuild(options.PackageNames);
			}
			_logger.WriteInfo("Done");
			return 0;
		} catch (Exception e) {
			// GetReadableMessageException, not .Message: a monitoring failure arrives as a chain whose outer
			// link only names the operation, and .Message printed "could not be monitored" without the reason.
			_logger.WriteError(e.GetReadableMessageException());
			return 1;
		}
	}

	#endregion

}
