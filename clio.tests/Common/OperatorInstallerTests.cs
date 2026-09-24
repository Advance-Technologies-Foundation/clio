using System;
using System.Collections.Generic;
using System.IO;
using AbstractionsFileSystem = System.IO.Abstractions.IFileSystem;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Command;
using Clio.Common;
using Clio.Common.OperatorBootstrap;
using Clio.Common.RuntimeAttachment;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture, Category("Unit"), Property("Module", "Common")]
public class OperatorInstallerTests {
	private IAttachmentProcess _process;
	private IOperatorInstaller _installer;
	private ServiceProvider _container;
	private readonly List<(string[] Args, string Input)> _calls = [];
	private string _existingInfrastructure;
	private bool _existingSecret;
	private string _targetUid;
	private bool _existingServices;
	private bool _foreignBinding;

	[SetUp]
	public void SetUp() {
		_calls.Clear();
		_existingInfrastructure = "";
		_existingSecret = false;
		_targetUid = "local-cluster";
		_existingServices = false;
		_foreignBinding = false;
		_process = Substitute.For<IAttachmentProcess>();
		_process.Run("rdctl", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>())
			.Returns("{\"containerEngine\":{\"name\":\"moby\"}}");
		_process.Run("kubectl", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>()).Returns(call => {
			string[] args = call.ArgAt<IReadOnlyList<string>>(1).ToArray();
			_calls.Add((args, call.ArgAt<string>(2)));
			if (args.Contains("kube-system")) { return args[1] == "rancher-desktop" ? "local-cluster" : _targetUid; }
			if (args.Contains("secret")) { return _existingSecret ? "secret/admin" : ""; }
			if (args.Contains("service")) { return _existingServices ? "service/existing" : ""; }
			if (args.Contains("ClusterRoleBinding") && _foreignBinding) { return "{\"metadata\":{}}"; }
			if (args.Contains("get") && args.Contains("default")) { return _existingInfrastructure; }
			if (args.Contains("get") && args.Contains("crd")) { return "crd/shared"; }
			return "";
		});
		MockFileSystem files = new();
		string root = Path.Combine(AppContext.BaseDirectory, "tpl", "operator", "rancher-desktop");
		files.AddFile(Path.Combine(root, "crds.json"), new MockFileData("{\"items\":[{\"kind\":\"CustomResourceDefinition\",\"metadata\":{\"name\":\"test\"}}]}"));
		files.AddFile(Path.Combine(root, "operator.json"), new MockFileData("""
		{"items":[
		 {"kind":"ClusterRoleBinding","metadata":{"name":"creatio-operator"}},
		 {"kind":"Deployment","metadata":{"name":"creatio-operator","namespace":"creatio-system"},
		  "spec":{"template":{"spec":{"containers":[{"image":"__OPERATOR_IMAGE__"}]}}}}
		]}
		"""));
		files.AddFile(Path.Combine(root, "provenance.json"), new MockFileData("{\"image\":\"test:1\"}"));
		_container = new ServiceCollection().AddSingleton(_process).AddSingleton<AbstractionsFileSystem>(files)
			.AddSingleton(Substitute.For<ILogger>()).AddTransient<IRancherDesktopTarget, RancherDesktopTarget>().AddTransient<IOperatorInstaller, OperatorInstaller>().BuildServiceProvider();
		_installer = _container.GetRequiredService<IOperatorInstaller>();
	}

	[TearDown]
	public void TearDown() => _container.Dispose();

	private static InstallOperatorOptions Options() => new() { Target = "rancher-desktop", Context = "local-alias" };

	[Test, Description("Invalid profiles fail before any cluster calls.")]
	public void Install_ShouldRejectUnsupportedTarget_BeforeMutation() {
		// Arrange
		InstallOperatorOptions options = Options(); options.Target = "remote";
		// Act
		Action act = () => _installer.Install(options);
		// Assert
		act.Should().Throw<ArgumentException>("only the supported profile may be installed");
		_calls.Should().BeEmpty("validation precedes cluster access");
	}

	[Test, Description("A Rancher profile cannot accidentally install into a remote context.")]
	public void Install_ShouldRejectDifferentCluster_WhenContextDoesNotMatchRancher() {
		// Arrange
		_targetUid = "other-cluster";
		// Act
		Action act = () => _installer.Install(Options());
		// Assert
		act.Should().Throw<InvalidOperationException>("the Docker socket belongs to the local Rancher cluster");
		_calls.Should().OnlyContain(c => c.Input == null, "cluster identity must be verified before writes");
	}

	[Test, Description("The shared policy disables Nexus before the operator starts; mutations all use the selected context.")]
	public void Install_ShouldCreateRegistryFreePolicy_BeforeStartingOperator() {
		// Arrange
		InstallOperatorOptions options = Options();
		// Act
		_installer.Install(options);
		// Assert
		var writes = _calls.Where(c => c.Input != null).ToList();
		writes.Should().OnlyContain(c => c.Args[0] == "--context" && c.Args[1] == "local-alias", "writes must never follow ambient current-context");
		int policy = writes.FindIndex(c => c.Input.Contains("CreatioSharedInfrastructure"));
		policy.Should().BeGreaterThanOrEqualTo(0, "the default policy must be created explicitly");
		JObject.Parse(writes[policy].Input)["spec"]["nexus"].Value<string>("mode").Should().Be("External", "no Nexus may be provisioned");
		policy.Should().BeLessThan(writes.Count - 1, "policy must exist before the controller starts");
		_calls.Should().NotContain(c => c.Args.Contains("delete") || c.Args.Contains("use-context"), "bootstrap preserves data and ambient context");
	}

	[Test, Description("Retry preserves both shared infrastructure configuration and dashboard credentials.")]
	public void Install_ShouldPreserveExistingObjects_WhenRetrying() {
		// Arrange
		_existingSecret = true;
		_existingInfrastructure = "{\"spec\":{\"nexus\":{\"mode\":\"External\"}}}";
		// Act
		_installer.Install(Options());
		// Assert
		_calls.Should().NotContain(c => c.Input != null && (c.Input.Contains("stringData") || c.Input.Contains("CreatioSharedInfrastructure")),
			"retry must not replace secrets or shared policy");
	}

	[Test, Description("An existing managed Nexus policy requires a deliberate migration.")]
	public void Install_ShouldRejectManagedNexus_BeforeMutation() {
		// Arrange
		_existingInfrastructure = "{\"spec\":{\"nexus\":{\"mode\":\"Managed\"}}}";
		// Act
		Action act = () => _installer.Install(Options());
		// Assert
		act.Should().Throw<InvalidOperationException>("bootstrap must not silently migrate infrastructure");
		_calls.Should().OnlyContain(c => c.Input == null, "conflicting policy is detected before writes");
	}

	[Test, Description("Existing shared services remain external even when their PVC layout differs from the operator default.")]
	public void Install_ShouldSelectExternalMode_WhenServicesAlreadyExist() {
		// Arrange
		_existingServices = true;
		// Act
		_installer.Install(Options());
		// Assert
		JObject policy = JObject.Parse(_calls.Single(c => c.Input?.Contains("CreatioSharedInfrastructure") == true).Input);
		policy["spec"]["postgres"].Value<string>("mode").Should().Be("External", "existing PostgreSQL data must not be adopted");
		policy["spec"]["redis"].Value<string>("mode").Should().Be("External", "existing Redis must remain under its current ownership");
	}

	[Test, Description("A retained binding from a different installation cannot be overwritten even if the Deployment is absent.")]
	public void Install_ShouldRejectForeignBinding_BeforeMutation() {
		// Arrange
		_foreignBinding = true;
		// Act
		Action act = () => _installer.Install(Options());
		// Assert
		act.Should().Throw<InvalidOperationException>("cluster-wide permissions must not be reassigned");
		_calls.Should().OnlyContain(c => c.Input == null, "ownership is checked before changing any resource");
	}

	[Test, Description("Image overrides are inserted as JSON data rather than executable manifest text.")]
	public void Install_ShouldUseImageOverride_WhenSupplied() {
		// Arrange
		InstallOperatorOptions options = Options(); options.Image = "registry.example/operator:2";
		// Act
		_installer.Install(options);
		// Assert
		string deployment = _calls.Last(c => c.Input != null).Input;
		deployment.Should().Contain(options.Image, "the distribution registry must be replaceable");
		deployment.Should().NotContain("__OPERATOR_IMAGE__", "the placeholder cannot reach Kubernetes");
	}

	[Test, Description("A secret API failure does not expose credentials in command errors.")]
	public void Install_ShouldRedactSecretFailure_WhenKubectlRejectsSecret() {
		// Arrange
		_process.Run("kubectl", Arg.Any<IReadOnlyList<string>>(), Arg.Is<string>(s => s != null && s.Contains("stringData")))
			.Returns<string>(_ => throw new InvalidOperationException("Rejected password=secret-value"));
		// Act
		Action act = () => _installer.Install(Options());
		// Assert
		act.Should().Throw<InvalidOperationException>().WithMessage("Could not create*", "raw kubectl errors can contain the secret object");
	}
}
