using Autofac;
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
	override protected void AdditionalRegistrations(ContainerBuilder containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.Register<Kubernetes>(provider => {
			throw new KubeConfigException("Kubernetes cannot be recognized.");
		}).As<IKubernetes>();
	}

	[Test]
	public void CreateK8SCommand_shouldthrowexception_ifKubernetesCannotBeRecognized() {
		Action act = () => Container.Resolve<k8Commands>();
		act.Should().Throw<Exception>().WithInnerException<KubeConfigException>()
			.Which.Message.Should().BeEquivalentTo("Kubernetes cannot be recognized.");
	}
}