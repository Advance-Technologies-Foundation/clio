using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Common.RuntimeAttachment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Common.OperatorBootstrap;

/// <summary>Uses operator custom resources rather than a separate environment inventory.</summary>
public interface IOperatorRuntimeService {
	/// <summary>Creates, lists or reads a runtime using the explicitly selected context and namespace.</summary>
	void Execute(RuntimeOptions options);
}

/// <inheritdoc/>
public class OperatorRuntimeService(IAttachmentProcess process, ILogger logger, ILocalRuntimeImageBuilder builder) : IOperatorRuntimeService {
	private const string Resource = "creatioinstances.apps.creatio.io";

	/// <inheritdoc/>
	public void Execute(RuntimeOptions options) {
		if (options.Action == "build") { builder.Build(options); return; }
		Validate(options);
		logger.WriteInfo($"Runtime target: context={options.Context}, namespace={options.Namespace}");
		if (options.Action == "create") {
			if (options.Name.Length > 55 || options.Name[0] < 'a' || options.Name[0] > 'z') {
				throw new ArgumentException("New runtime names must start with a letter and be at most 55 characters to allow the operator's Service suffix.");
			}
			Create(options);
			return;
		}
		List<string> args = ["get", Resource, "-n", options.Namespace, "-o", "json"];
		if (options.Action == "status") { args.Add(options.Name); }
		JObject result = JObject.Parse(Kubectl(options.Context, args));
		IEnumerable<JToken> instances = options.Action == "list" ? result["items"].Children() : new[] { result };
		foreach (JToken instance in instances) {
			JToken status = instance["status"];
			logger.WriteInfo($"{options.Context}/{options.Namespace}/{instance["metadata"]["name"]}: {status?["phase"] ?? "Pending"}");
			if (status?["serviceUrl"] is JToken url) { logger.WriteInfo($"Creatio: {url}"); }
			if (status?["clioMcpUrl"] is JToken mcp) { logger.WriteInfo($"Clio MCP: {mcp}"); }
			if (status?["message"] is JToken message) { logger.WriteInfo(message.ToString()); }
		}
	}

	private static void Validate(RuntimeOptions options) {
		if (options.Action is not ("create" or "list" or "status")) {
			throw new ArgumentException("Runtime action must be build, create, list or status.");
		}
		if (string.IsNullOrWhiteSpace(options.Context)) { throw new ArgumentException("Specify --context explicitly."); }
		ValidateName(options.Namespace, "namespace");
		if (options.Action != "list") { ValidateName(options.Name, "name"); }
		if (options.Action == "create") {
			string image = options.Image;
			if (string.IsNullOrWhiteSpace(image) || image.Any(char.IsWhiteSpace) || image.Contains('@') ||
				image.LastIndexOf(':') <= image.LastIndexOf('/') || image.EndsWith(':')) {
				throw new ArgumentException("Create requires --image <repository>:<tag> available in the selected cluster.");
			}
		}
	}

	private static void ValidateName(string value, string field) {
		if (string.IsNullOrWhiteSpace(value) || value.Length > 63 ||
			!Regex.IsMatch(value, "^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) {
			throw new ArgumentException($"Runtime {field} must be a lowercase Kubernetes name, at most 63 characters.");
		}
	}

	private void Create(RuntimeOptions options) {
		Kubectl(options.Context, ["get", "crd", Resource, "-o", "name"]);
		if (string.IsNullOrWhiteSpace(Kubectl(options.Context, ["get", "namespace", options.Namespace, "--ignore-not-found", "-o", "name"]))) {
			Kubectl(options.Context, ["create", "namespace", options.Namespace]);
		}
		int separator = options.Image.LastIndexOf(':');
		JObject resource = new() {
			["apiVersion"] = "apps.creatio.io/v1alpha1", ["kind"] = "CreatioInstance",
			["metadata"] = new JObject { ["name"] = options.Name, ["namespace"] = options.Namespace },
			["spec"] = new JObject { ["image"] = new JObject {
				["repository"] = options.Image[..separator], ["tag"] = options.Image[(separator + 1)..],
				["databaseSourceMode"] = "template" } }
		};
		// Create, never apply: a reused name must not silently replace an existing runtime.
		Kubectl(options.Context, ["create", "-f", "-"], resource.ToString(Formatting.None));
		logger.WriteInfo($"Runtime creation requested: {options.Context}/{options.Namespace}/{options.Name}. Provisioning continues in the operator.");
		logger.WriteInfo($"Inspect with: clio runtime status {options.Name} --context {options.Context} --namespace {options.Namespace}");
	}

	private string Kubectl(string context, IReadOnlyList<string> args, string input = null) =>
		process.Run("kubectl", new[] { "--context", context }.Concat(args).ToArray(), input);
}
