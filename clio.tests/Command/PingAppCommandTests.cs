using Autofac;
using Clio.Command;
using Creatio.Client;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command
{

	[TestFixture]
	internal class PingAppCommandTests : BaseClioModuleTests
	{

		private ICreatioClient creatioClient = NSubstitute.Substitute.For<ICreatioClient>();

		protected override void AdditionalRegistrations(ContainerBuilder containerBuilder) {
			containerBuilder.RegisterInstance(creatioClient).As<ICreatioClient>();
			base.AdditionalRegistrations(containerBuilder);
		}

		[Test]
		public void PingAppCommandShoulBeUsesAllRetryOptions() {
			PingAppCommand command = _container.Resolve<PingAppCommand>();
			PingAppOptions options = new PingAppOptions() { TimeOut = 100, RetryCount = 2, RetryDelay = 300 };
			command.Execute(options);

			//Assert
			creatioClient.Received(1).ExecuteGetRequest(Arg.Any<string>(), 100, 2, 300);
		}
	}

}
