using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Common.OperatorBootstrap;
using Clio.Common.RuntimeAttachment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Common.Studio;

/// <summary>Submits resolved handoffs to the operator without cloning or building sources.</summary>
public interface IStudioDeploymentService {
	/// <summary>Resolves inputs and submits, or returns InputRequired before writes.</summary>
	JObject Deploy(JObject profile, StudioOptions options);
	/// <summary>Reads the operator's persisted installation status.</summary>
	JObject Status(StudioOptions options);
}

/// <inheritdoc/>
public class StudioDeploymentService(IAttachmentProcess process, IRancherDesktopTarget target, IOperatorInstaller installer) : IStudioDeploymentService {
	private const string Resource = "creatioaistudios.apps.creatio.io";
	private const string OwnerLabel = "apps.creatio.io/studio-uid";
	private const string ProfileLabel = "apps.creatio.io/studio-name";

	/// <inheritdoc/>
	public JObject Deploy(JObject profile, StudioOptions options) {
		if (profile["deployment"] is not JObject deployment) throw new ArgumentException("Deploy requires a deployment object.");
		JObject supplied = ReadInputs(options.Inputs);
		var initial = StudioInputs.Resolve(profile, supplied, new JObject(), false);
		// Namespace must be known before retrieving previously supplied values.
		// Other missing inputs are reported after the read-only lookup, before writes.
		if (initial.Missing.Count > 0 && deployment["namespace"]?.Type == JTokenType.String &&
			initial.Missing.Any(m => deployment.Value<string>("namespace").Contains("${" + m.Value<string>("name") + "}")))
			return new JObject { ["state"] = "InputRequired", ["missingInputs"] = initial.Missing };
		target.Verify(options.Context);
		string name = profile.Value<string>("name");
		string ns = StudioInputs.Expand(deployment["namespace"] ?? throw new ArgumentException("Deployment namespace is required."), initial.Values).Value<string>();
		StudioProfile.ValidateName(ns, "namespace");
		string secretName = name + "-handoff";
		JObject existingNamespace = Read(options.Context, "namespace", ns);
		if (existingNamespace is not null && existingNamespace["metadata"]?["labels"]?.Value<string>(ProfileLabel) != name) throw new InvalidOperationException("Deployment namespace is not owned by this Studio installation.");
		JObject oldSecret = existingNamespace is null ? null : Read(options.Context, "secret", secretName, ns);
		JObject current = existingNamespace is null ? null : Read(options.Context, Resource, name, ns);
		if (current is not null && current["metadata"]?["labels"]?.Value<string>(ProfileLabel) != name) throw new InvalidOperationException("Studio resource belongs to another installation.");
		if (oldSecret is not null && (string.IsNullOrEmpty(current?["metadata"]?.Value<string>("uid")) ||
			oldSecret["metadata"]?["labels"]?.Value<string>(OwnerLabel) != current["metadata"].Value<string>("uid")))
			throw new InvalidOperationException("Handoff Secret belongs to another installation.");
		JObject previous = oldSecret?["data"]?.Value<string>("inputs.json") is string encoded ? JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(encoded))) : new JObject();
		var resolved = StudioInputs.Resolve(profile, supplied, previous, true);
		if (resolved.Missing.Count > 0) return new JObject { ["state"] = "InputRequired", ["missingInputs"] = resolved.Missing };
		string json = StudioInputs.Expand(deployment, resolved.Values).ToString(Formatting.None);
		if (Encoding.UTF8.GetByteCount(json) + Encoding.UTF8.GetByteCount(resolved.Values.ToString(Formatting.None)) > StudioProfile.MaximumBytes) throw new ArgumentException("Resolved deployment and inputs exceed 800 KiB.");
		string revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
		EnsureOperator(profile, options);
		if (existingNamespace is null) Create(options.Context, new JObject { ["apiVersion"] = "v1", ["kind"] = "Namespace", ["metadata"] = new JObject { ["name"] = ns, ["labels"] = new JObject { [ProfileLabel] = name } } });
		JObject cr = new() {
			["apiVersion"] = "apps.creatio.io/v1alpha1", ["kind"] = "CreatioAiStudio",
			["metadata"] = new JObject { ["name"] = name, ["namespace"] = ns, ["labels"] = new JObject { [ProfileLabel] = name } },
			["spec"] = new JObject { ["handoffSecret"] = secretName, ["handoffRevision"] = revision }
		};
		bool created = current is null;
		if (created) current = Create(options.Context, cr);
		string uid = current["metadata"].Value<string>("uid");
		JObject secret = new() {
			["apiVersion"] = "v1", ["kind"] = "Secret", ["type"] = "Opaque",
			// Keep credentials with retained storage. Recreating a deleted installation
			// requires explicit recovery; the UID check prevents silent reassignment.
			["metadata"] = new JObject { ["name"] = secretName, ["namespace"] = ns, ["labels"] = new JObject { [OwnerLabel] = uid } },
			["data"] = new JObject { ["deployment.json"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)), ["inputs.json"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(resolved.Values.ToString(Formatting.None))) }
		};
		if (oldSecret is null) Create(options.Context, secret);
		else {
			secret["metadata"]["resourceVersion"] = oldSecret["metadata"]["resourceVersion"];
			Kube(options.Context, ["replace", "-f", "-", "-o", "name"], secret.ToString(Formatting.None));
		}
		if (!created) {
			// Status writes advance resourceVersion independently. Test installation
			// identity and the previous spec instead, so status cannot race submission.
			var patch = new JArray(
				new JObject { ["op"] = "test", ["path"] = "/metadata/uid", ["value"] = uid },
				new JObject { ["op"] = "test", ["path"] = "/metadata/labels/apps.creatio.io~1studio-name", ["value"] = name },
				new JObject { ["op"] = "test", ["path"] = "/spec", ["value"] = current["spec"].DeepClone() },
				new JObject { ["op"] = "replace", ["path"] = "/spec", ["value"] = cr["spec"].DeepClone() });
			Kube(options.Context, ["patch", Resource, name, "-n", ns, "--type=json", "-p", patch.ToString(Formatting.None), "-o", "name"]);
		}
		return new JObject { ["state"] = "Submitted", ["name"] = name, ["namespace"] = ns, ["context"] = options.Context, ["revision"] = revision };
	}

	/// <inheritdoc/>
	public JObject Status(StudioOptions options) {
		if (string.IsNullOrWhiteSpace(options.Context)) throw new ArgumentException("Specify --context.");
		StudioProfile.ValidateName(options.Name, "name");
		StudioProfile.ValidateName(options.Namespace, "namespace");
		JObject resource = Read(options.Context, Resource, options.Name, options.Namespace) ?? throw new InvalidOperationException("Studio installation does not exist.");
		return new JObject { ["name"] = options.Name, ["namespace"] = options.Namespace, ["status"] = resource["status"]?.DeepClone() ?? new JObject { ["phase"] = "Pending" } };
	}

	private void EnsureOperator(JObject profile, StudioOptions options) {
		JObject crd = Read(options.Context, "crd", Resource);
		JObject controller = Read(options.Context, "deployment", "creatio-operator", "creatio-system");
		string image = profile["operator"]?.Value<string>("image");
		if (image is not null && !Regex.IsMatch(image, @"^[^\s]+@sha256:[a-f0-9]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
			throw new ArgumentException("The handoff operator image must be pinned by sha256 digest.");
		if (image is not null && image != JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tpl", "operator", "rancher-desktop", "provenance.json"))).Value<string>("image"))
			throw new InvalidOperationException("The handoff requests an operator image outside this Clio release. Install a matching Clio release or have an administrator explicitly manage the operator; a handoff cannot select arbitrary cluster-wide controller code.");
		bool SupportsStudio() => crd?["metadata"]?["annotations"]?.Value<string>("apps.creatio.io/studio-handoff-schema") == StudioProfile.Schema &&
			controller?["spec"]?["template"]?["metadata"]?["annotations"]?.Value<string>("apps.creatio.io/studio-handoff-schema") == StudioProfile.Schema;
		if (SupportsStudio() && image is not null && controller?["spec"]?["template"]?["spec"]?["containers"]?.Any(c => c.Value<string>("image") == image) != true)
			throw new InvalidOperationException("The installed Studio-capable operator differs from the handoff's requested release. Resolve that version choice explicitly; Studio deploy does not replace an already compatible controller.");
		bool needsKeda = profile["deployment"]?["dependencies"] is JArray dependencies && dependencies.Values<string>().Contains("keda-2.20.2");
		bool missingKedaRoles = needsKeda && (new[] { "keda-operator", "keda-operator-minimal-cluster-role", "keda-operator-external-metrics-reader", "keda-operator-webhook" }.Any(n => Read(options.Context, "clusterrole", n) is null) ||
			Read(options.Context, "role", "keda-operator-certs", "keda") is null);
		if (!SupportsStudio() || missingKedaRoles) {
			if (controller is not null && controller["metadata"]?["annotations"]?.Value<string>(OperatorInstaller.Annotation) != OperatorInstaller.Profile)
				throw new InvalidOperationException("Existing operator is outside the Clio installation profile or lacks the requested Studio release. An administrator must upgrade it with the matching operator bootstrap and image, preserving its configuration; see docs/studio-handoff.md in creatio-operator. No adoption was attempted.");
			string retainedImage = SupportsStudio() ? controller?["spec"]?["template"]?["spec"]?["containers"]?.FirstOrDefault()?.Value<string>("image") : null;
			installer.Install(new InstallOperatorOptions { Target = "rancher-desktop", Context = options.Context, Image = retainedImage ?? image, IncludeStudioDependencies = needsKeda });
			crd = Read(options.Context, "crd", Resource);
			controller = Read(options.Context, "deployment", "creatio-operator", "creatio-system");
			if (!SupportsStudio()) throw new InvalidOperationException("Operator bootstrap did not install the requested Studio controller capability and CRD.");
		}
		// A CRD can survive a failed or interrupted controller rollout.
		Kube(options.Context, ["rollout", "status", "deployment/creatio-operator", "-n", "creatio-system", "--timeout=110s"]);
	}

	private static JObject ReadInputs(string path) {
		if (string.IsNullOrWhiteSpace(path)) return new JObject();
		if (new FileInfo(path).Length > StudioProfile.MaximumBytes) throw new ArgumentException("Studio inputs exceed 800 KiB.");
		try { return JObject.Parse(File.ReadAllText(path), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error }); }
		catch (JsonException) { throw new ArgumentException("Inputs file must contain a JSON object."); }
	}
	private JObject Read(string context, string kind, string name, string ns = null) {
		List<string> args = ["get", kind, name, "--ignore-not-found", "-o", "json"];
		if (ns is not null) args.AddRange(["-n", ns]);
		string json = Kube(context, args);
		return string.IsNullOrWhiteSpace(json) ? null : JObject.Parse(json);
	}
	private JObject Create(string context, JObject resource) => JObject.Parse(Kube(context, ["create", "-f", "-", "-o", "json"], resource.ToString(Formatting.None)));
	private string Kube(string context, IReadOnlyList<string> args, string input = null) {
		try { return process.Run("kubectl", new[] { "--context", context, "--request-timeout=30s" }.Concat(args).ToArray(), input); }
		catch (InvalidOperationException) { throw new InvalidOperationException("Studio Kubernetes request failed. Check context, permissions, resource ownership and operator status. Response bodies are suppressed to protect credentials."); }
	}
}
