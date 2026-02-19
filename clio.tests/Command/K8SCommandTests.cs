using Clio.Common.K8;
using FluentAssertions;
using k8s;
using k8s.Exceptions;
using NUnit.Framework;
using System;


namespace Clio.Tests.Command;

[TestFixture]
internal class K8SCommandTests : BaseClioModuleTests
{
	override protected void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddTransient<IKubernetes>(provider => {
			throw new KubeConfigException("Kubernetes cannot be recognized.");
		});
	}

	[Test]
	public void CreateK8SCommand_shouldthrowexception_ifKubernetesCannotBeRecognized() {
		Action act = () => Container.GetRequiredService<k8Commands>();
		var exceptionAssertions = act.Should().Throw<Exception>();
		var thrown = exceptionAssertions.Which;
		var message = thrown.InnerException is KubeConfigException inner ? inner.Message : thrown.Message;
		message.Should().BeEquivalentTo("Kubernetes cannot be recognized.");
	}
}
