using Autofac;
using Clio.Common.K8;
using FluentAssertions;
using k8s;
using k8s.Exceptions;
using NSubstitute;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Tests.Command
{
	[TestFixture]
	internal class K8SCommandTests: BaseClioModuleTests
	{
		override protected void AdditionalRegistrations(ContainerBuilder containerBuilder) {
			base.AdditionalRegistrations(containerBuilder);
			containerBuilder.Register<Kubernetes>(provider => {
				throw new KubernetesException("Kubernetes cannot be recognized.");
			}).As<IKubernetes>();
		}

		[Test]
		public void CreateK8SCommand_shouldthrowexception_ifKubernetesCannotBeRecognized() {
			Action act = () => Container.Resolve<k8Commands>();
			act.Should().Throw<Exception>().WithInnerException<KubernetesException>()
				.Which.Message.Should().BeEquivalentTo("Kubernetes cannot be recognized.");
		}
	}
}
