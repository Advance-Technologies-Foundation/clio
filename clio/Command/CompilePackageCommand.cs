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
	/// Warns the interactive user that compilation is a heavy operation and asks whether to proceed now
	/// or postpone (ENG-93157). Fails <b>open</b>: a non-interactive host (the MCP server, CI, redirected
	/// stdin) returns <see langword="true"/> without prompting, so the confirmed-compile behavior is
	/// unchanged for those callers.
	/// </summary>
	private bool ConfirmCompilation(CompilePackageOptions options) {
		if (_interactiveConsole.ConfirmOrProceedWhenNonInteractive(PackageCompilationWarning)) {
			return true;
		}
		_logger.WriteInfo(BuildPostponeHint(options));
		return false;
	}

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
		if (!ConfirmCompilation(options)) {
			// The user chose to postpone: nothing is compiled and this is a deliberate choice, not an
			// error, so the command exits 0. Only reachable on an interactive terminal — non-interactive
			// hosts (the MCP server that runs this same command, CI, piped stdin) proceed without asking.
			return 0;
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
