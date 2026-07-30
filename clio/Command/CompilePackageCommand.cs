using System;
using Clio.Common;
using Clio.Package;
using CommandLine;

namespace Clio.Command;

[Verb("compile-package", Aliases = ["comp-pkg"], HelpText = "Build package command")]
public class CompilePackageOptions : EnvironmentNameOptions
{

	#region Properties: Public


	[Value(0, MetaName = "PackageName", Required = true, HelpText = "Specified package name")]
	public string PackageName
	{
		get; set;
	}

	public string[] PackageNames => PackageName.Split(',');

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
		return $"Compilation postponed. Nothing was compiled. Run it later with: clio compile-package {options.PackageName}{environmentPart}";
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
		try {
			_packageBuilder.Rebuild(options.PackageNames);
			_logger.WriteInfo("Done");
			return 0;
		} catch (Exception e) {
			_logger.WriteError(e.Message);
			return 1;
		}
	}

	#endregion

}
