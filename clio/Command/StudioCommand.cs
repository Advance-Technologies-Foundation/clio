using System;
using System.IO;
using Clio.Common;
using Clio.Common.Studio;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Command;

/// <summary>Independent operations over a portable AI Studio handoff.</summary>
[Verb("studio", HelpText = "Deploy AI Studio through the operator, inspect status, or independently check out source worktrees.")]
[FeatureToggle(ExperimentalFeature.Runtime)]
public class StudioOptions {
	/// <summary>Operation: deploy, checkout or status.</summary>
	[Value(0, Required = true)] public string Action { get; set; }
	/// <summary>Portable JSON handoff file, required for deploy and checkout.</summary>
	[Option("profile")] public string Profile { get; set; }
	/// <summary>Destination for source worktrees, required for checkout.</summary>
	[Option("directory")] public string Directory { get; set; }
	/// <summary>JSON input values file for deployment; never passed in command-line values.</summary>
	[Option("inputs")] public string Inputs { get; set; }
	/// <summary>Explicit Kubernetes context for deploy and status.</summary>
	[Option("context")] public string Context { get; set; }
	/// <summary>Installation namespace, required for status.</summary>
	[Option("namespace")] public string Namespace { get; set; }
	/// <summary>Installation name, required for status.</summary>
	[Option("name")] public string Name { get; set; }
}

/// <summary>Returns structured results without implicit coupling between deployment and sources.</summary>
public class StudioCommand(IStudioCheckout checkout, IStudioDeploymentService deployment, ILogger logger) : Command<StudioOptions> {
	/// <inheritdoc/>
	public override int Execute(StudioOptions options) {
		try {
			JObject result = options.Action switch {
				"checkout" => checkout.Checkout(StudioProfile.Read(options.Profile), options.Directory),
				"deploy" => deployment.Deploy(StudioProfile.Read(options.Profile), options),
				"status" => deployment.Status(options),
				_ => throw new ArgumentException("Studio action must be deploy, checkout or status.")
			};
			logger.WriteInfo(result.ToString(Formatting.Indented));
			return result.Value<string>("state") == "InputRequired" ? 2 : 0;
		} catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidCastException) {
			// JSON conversion failures can quote the original value; never report them verbatim.
			string message = ex is JsonException or FormatException or InvalidCastException ? "Invalid Studio profile or inputs value." : ex.Message;
			logger.WriteError(new JObject { ["state"] = "Failed", ["message"] = message }.ToString(Formatting.None));
			return 1;
		}
	}
}
