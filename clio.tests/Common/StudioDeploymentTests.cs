using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Common.OperatorBootstrap;
using Clio.Common.RuntimeAttachment;
using Clio.Common.Studio;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture, Category("Unit"), Property("Module", "Common")]
public class StudioDeploymentTests {
	private ServiceProvider _container;
	private IStudioDeploymentService _service;
	private IOperatorInstaller _installer;
	private readonly Dictionary<string, JObject> _objects = [];
	private readonly List<JObject> _writes = [];

	[SetUp]
	public void SetUp() {
		_objects.Clear(); _writes.Clear();
		_objects["deployment/creatio-operator"] = CompatibleController();
		var process = Substitute.For<IAttachmentProcess>();
		_installer = Substitute.For<IOperatorInstaller>();
		process.Run("kubectl", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>()).Returns(call => {
			var args = call.ArgAt<IReadOnlyList<string>>(1).Skip(3).ToArray();
			if (args[0] == "get") {
				if (args[1] == "crd" && !_objects.ContainsKey("crd/creatioaistudios.apps.creatio.io")) return """{"metadata":{"annotations":{"apps.creatio.io/studio-handoff-schema":"creatio-studio-handoff/v1"}}}""";
				return _objects.TryGetValue(args[1] + "/" + args[2], out var found) ? found.ToString() : "";
			}
			if (args[0] == "rollout") return "ready";
			if (args[0] == "patch") {
				var patch = JArray.Parse(args[Array.IndexOf(args, "-p") + 1]);
				var current = _objects[args[1] + "/" + args[2]];
				patch[0].Value<string>("path").Should().Be("/metadata/uid", "spec publication must retain identity preconditions");
				current["spec"] = patch.Last["value"].DeepClone();
				_writes.Add((JObject)current.DeepClone());
				return current.ToString();
			}
			var resource = JObject.Parse(call.ArgAt<string>(2));
			resource["metadata"]["uid"] ??= "test-uid";
			resource["metadata"]["resourceVersion"] = "1";
			_writes.Add(resource);
			string kind = resource.Value<string>("kind") == "CreatioAiStudio" ? "creatioaistudios.apps.creatio.io" : resource.Value<string>("kind").ToLowerInvariant();
			_objects[kind + "/" + resource["metadata"].Value<string>("name")] = resource;
			// A real controller can publish status after the CR is created or read,
			// before Clio finishes writing the handoff Secret.
			if (kind == "secret" && _objects.TryGetValue("creatioaistudios.apps.creatio.io/demo", out var cr)) cr["metadata"]["resourceVersion"] = "status-advanced";
			return resource.ToString();
		});
		_container = new ServiceCollection().AddSingleton(process).AddSingleton(_installer)
			.AddSingleton(Substitute.For<IRancherDesktopTarget>()).AddTransient<IStudioDeploymentService, StudioDeploymentService>().BuildServiceProvider();
		_service = _container.GetRequiredService<IStudioDeploymentService>();
	}
	[TearDown] public void TearDown() => _container.Dispose();
	private static JObject CompatibleController() => JObject.Parse("""{"metadata":{"annotations":{"clio.creatio.com/install-profile":"rancher-desktop-v1"}},"spec":{"template":{"metadata":{"annotations":{"apps.creatio.io/studio-handoff-schema":"creatio-studio-handoff/v1"}}}}}""");
	private static JObject Profile() => JObject.Parse("""{"name":"demo","inputs":{"password":{"generate":"password"}},"deployment":{"namespace":"studio-demo","phases":[{"name":"config","resources":[{"apiVersion":"v1","kind":"Secret","metadata":{"name":"app","namespace":"studio-demo"},"stringData":{"password":"${password}"}}]}]}}""");
	private static StudioOptions Options() => new() { Context = "rancher-desktop" };

	[Test, Description("Missing required values return actionable input descriptors before any cluster mutation.")]
	public void MissingInputs_ShouldPreventMutation() {
		var profile = Profile(); profile["inputs"]["providerKey"] = new JObject { ["required"] = true, ["secret"] = true };
		var result = _service.Deploy(profile, Options());
		result.Value<string>("state").Should().Be("InputRequired", "the agent must collect the omitted value");
		_writes.Should().BeEmpty("missing inputs must not leave a partial installation");
		_installer.DidNotReceive().Install(Arg.Any<InstallOperatorOptions>());
	}

	[Test, Description("A repeated deploy preserves generated credentials and the deployment revision.")]
	public void Retry_ShouldPreserveSecretsAndRevision() {
		var first = _service.Deploy(Profile(), Options());
		string data = _objects["secret/demo-handoff"]["data"].ToString();
		var second = _service.Deploy(Profile(), Options());
		second.Value<string>("revision").Should().Be(first.Value<string>("revision"), "stable inputs must produce an identical desired revision");
		_objects["secret/demo-handoff"]["data"].ToString().Should().Be(data, "database credentials must survive a retry");
		_objects["secret/demo-handoff"]["metadata"]["ownerReferences"].Should().BeNull("credentials are retained alongside PVC data");
		_installer.DidNotReceive().Install(Arg.Any<InstallOperatorOptions>());
		_writes.Count(r => r.Value<string>("kind") == "CreatioAiStudio").Should().Be(2, "first creation needs no replace and retry publishes only spec despite concurrent status updates");
	}

	[Test, Description("A foreign namespace fails before bootstrap or writes.")]
	public void ForeignNamespace_ShouldRemainUnchanged() {
		_objects["namespace/studio-demo"] = JObject.Parse("""{"metadata":{"name":"studio-demo"}}""");
		Action act = () => _service.Deploy(Profile(), Options());
		act.Should().Throw<InvalidOperationException>("a handoff cannot adopt an unrelated namespace");
		_writes.Should().BeEmpty("foreign resources must remain unchanged");
	}

	[Test, Description("An unlabelled handoff Secret is foreign even when no Studio CR exists.")]
	public void ForeignSecretWithoutCr_ShouldPreventMutation() {
		_objects["namespace/studio-demo"] = JObject.Parse("""{"metadata":{"name":"studio-demo","labels":{"apps.creatio.io/studio-name":"demo"}}}""");
		_objects["secret/demo-handoff"] = JObject.Parse("""{"metadata":{"name":"demo-handoff"},"data":{}}""");
		Action act = () => _service.Deploy(Profile(), Options());
		act.Should().Throw<InvalidOperationException>("two absent UID values cannot prove ownership");
		_writes.Should().BeEmpty("the existing Secret must not be consumed or replaced");
	}
	[Test, Description("A surviving CRD does not prove a controller is installed; bootstrap repairs an interrupted installation.")]
	public void CrdWithoutController_ShouldBootstrap() {
		_objects.Remove("deployment/creatio-operator");
		_installer.When(i => i.Install(Arg.Any<InstallOperatorOptions>())).Do(_ => _objects["deployment/creatio-operator"] = CompatibleController());
		_service.Deploy(Profile(), Options()).Value<string>("state").Should().Be("Submitted", "bootstrap restores the missing controller");
		_installer.Received(1).Install(Arg.Any<InstallOperatorOptions>());
	}

	[Test, Description("A Clio-owned pre-Studio operator is upgraded by the existing installer.")]
	public void OwnedOldController_ShouldUpgrade() {
		_objects["deployment/creatio-operator"]["spec"]["template"]["metadata"]["annotations"] = new JObject();
		_installer.When(i => i.Install(Arg.Any<InstallOperatorOptions>())).Do(_ => _objects["deployment/creatio-operator"] = CompatibleController());
		_service.Deploy(Profile(), Options());
		_installer.Received(1).Install(Arg.Any<InstallOperatorOptions>());
	}

	[Test, Description("A foreign pre-Studio controller is not silently replaced or adopted.")]
	public void ForeignOldController_ShouldBlock() {
		_objects["deployment/creatio-operator"] = JObject.Parse("{} ");
		Action act = () => _service.Deploy(Profile(), Options());
		act.Should().Throw<InvalidOperationException>("existing administrator configuration must be migrated explicitly");
		_writes.Should().BeEmpty("a blocked bootstrap must not create Studio resources");
		_installer.DidNotReceive().Install(Arg.Any<InstallOperatorOptions>());
	}

	[Test, Description("The producer cannot select a mutable operator image for a reproducible handoff.")]
	public void MutableOperatorImage_ShouldPreventBootstrap() {
		var profile = Profile(); profile["operator"] = new JObject { ["image"] = "operator:latest" };
		Action act = () => _service.Deploy(profile, Options());
		act.Should().Throw<ArgumentException>("the operator release is part of the pinned installation contract");
		_writes.Should().BeEmpty("image validation must precede bootstrap and submission");
		_installer.DidNotReceive().Install(Arg.Any<InstallOperatorOptions>());
	}

	[Test, Description("A handoff cannot replace the cluster controller with an arbitrary digest-pinned image.")]
	public void UnbundledOperatorImage_ShouldPreventBootstrap() {
		var profile = Profile(); profile["operator"] = new JObject { ["image"] = "untrusted.invalid/operator@sha256:" + new string('a', 64) };
		Action act = () => _service.Deploy(profile, Options());
		act.Should().Throw<InvalidOperationException>("controller code must come from the installed Clio release, not arbitrary producer input");
		_writes.Should().BeEmpty("image selection must be settled before submission");
		_installer.DidNotReceive().Install(Arg.Any<InstallOperatorOptions>());
	}

}
