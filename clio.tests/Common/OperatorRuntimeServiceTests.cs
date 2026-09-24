using System;
using System.Collections.Generic;
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
public class OperatorRuntimeServiceTests {
	private IAttachmentProcess _process;
	private IOperatorRuntimeService _service;
	private ServiceProvider _container;
	private readonly List<(string[] Args, string Input)> _calls = [];

	[SetUp]
	public void SetUp() {
		_calls.Clear();
		_process = Substitute.For<IAttachmentProcess>();
		_process.Run("kubectl", Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>()).Returns(call => {
			_calls.Add((call.ArgAt<IReadOnlyList<string>>(1).ToArray(), call.ArgAt<string>(2)));
			return "";
		});
		_container = new ServiceCollection().AddSingleton(_process).AddSingleton(Substitute.For<ILogger>())
			.AddSingleton(Substitute.For<ILocalRuntimeImageBuilder>()).AddTransient<IOperatorRuntimeService, OperatorRuntimeService>().BuildServiceProvider();
		_service = _container.GetRequiredService<IOperatorRuntimeService>();
	}

	[TearDown]
	public void TearDown() => _container.Dispose();

	private static RuntimeOptions Options() => new() {
		Action = "create", Name = "test-runtime", Namespace = "dev", Context = "chosen-cluster", Image = "registry.example:5000/creatio-dev:1"
	};

	[Test, Description("All creation calls use the explicit cluster, and registry ports are distinguished from image tags.")]
	public void Execute_ShouldCreateInSelectedContext_WhenImageHasRegistryPort() {
		// Arrange
		RuntimeOptions options = Options();
		// Act
		_service.Execute(options);
		// Assert
		_calls.Should().OnlyContain(c => c.Args[0] == "--context" && c.Args[1] == options.Context, "ambient context must never select a deployment destination");
		var request = _calls.Single(c => c.Input != null);
		request.Args.Should().Contain("create", "existing runtimes must never be overwritten by apply");
		JObject.Parse(request.Input)["spec"]["image"].Value<string>("repository").Should().Be("registry.example:5000/creatio-dev", "registry ports are part of the repository");
		JObject.Parse(request.Input)["spec"]["image"].Value<string>("databaseSourceMode").Should().Be("template", "new runtimes need a restored database, not an empty application pod");
	}

	[Test, Description("Missing context is refused before accessing Kubernetes.")]
	public void Execute_ShouldRejectMissingContext_BeforeClusterAccess() {
		// Arrange
		RuntimeOptions options = Options(); options.Context = "";
		// Act
		Action act = () => _service.Execute(options);
		// Assert
		act.Should().Throw<ArgumentException>("runtime destination must be explicit");
		_calls.Should().BeEmpty("invalid target selection cannot cause mutations");
	}

	[TestCase("image"), TestCase("registry:5000/image"), TestCase("image:"), TestCase("image@sha256:abc")]
	[Description("Operator resource requires separate repository and tag; reject unsupported input before mutation.")]
	public void Execute_ShouldRejectInvalidImage_BeforeMutation(string image) {
		// Arrange
		RuntimeOptions options = Options(); options.Image = image;
		// Act
		Action act = () => _service.Execute(options);
		// Assert
		act.Should().Throw<ArgumentException>("the operator image contract requires a tag");
		_calls.Should().BeEmpty("invalid image references cannot create orphan namespaces");
	}

	[TestCase("1runtime"), TestCase("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcd")]
	[Description("Instance names must leave room for the operator's -service suffix.")]
	public void Execute_ShouldRejectInvalidServiceName_BeforeMutation(string name) {
		// Arrange
		RuntimeOptions options = Options(); options.Name = name;
		// Act
		Action act = () => _service.Execute(options);
		// Assert
		act.Should().Throw<ArgumentException>("a valid CR name can still be too long for generated Services");
		_calls.Should().BeEmpty("an unprovisionable instance should not be submitted");
	}
}
