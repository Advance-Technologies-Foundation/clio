using System;
using Clio.Common;
using Clio.Common.OperatorBootstrap;
using CommandLine;

namespace Clio.Command;

/// <summary>Installs the operator using the registry-free Rancher Desktop profile.</summary>
[Verb("install-operator", HelpText = "Install the Creatio operator into Rancher Desktop without Nexus.")]
public class InstallOperatorOptions {
	/// <summary>Installation profile. Currently rancher-desktop only.</summary>
	[Option("target", Required = true)]
	public string Target { get; set; }
	/// <summary>Kubernetes context, independent of the current context.</summary>
	[Option("context", Default = "rancher-desktop")]
	public string Context { get; set; }
	/// <summary>Optional operator image reference, overriding the bundled digest.</summary>
	[Option("image")]
	public string Image { get; set; }
}

/// <summary>Host-side operator bootstrap. Does not remove existing infrastructure.</summary>
public class InstallOperatorCommand(IOperatorInstaller installer, ILogger logger) : Command<InstallOperatorOptions> {
	/// <inheritdoc/>
	public override int Execute(InstallOperatorOptions options) {
		try {
			installer.Install(options);
			return 0;
		} catch (Exception ex) {
			logger.WriteError(ex.Message);
			return 1;
		}
	}
}
