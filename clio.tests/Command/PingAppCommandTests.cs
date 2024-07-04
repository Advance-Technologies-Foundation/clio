using Autofac;
using Clio.Command;
using Clio.Common;
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

		private IApplicationClient creatioClient = NSubstitute.Substitute.For<IApplicationClient>();

		protected override void AdditionalRegistrations(ContainerBuilder containerBuilder) {
			_environmentSettings.IsNetCore = true;
			base.AdditionalRegistrations(containerBuilder);
			containerBuilder.RegisterInstance(creatioClient).As<IApplicationClient>();
		}

		[Test]
		public void PingAppCommandShoulBeUsesAllRetryOptions() {
			PingAppCommand command = _container.Resolve<PingAppCommand>();
			PingAppOptions options = new PingAppOptions() { TimeOut = 1, RetryCount = 2, RetryDelay = 3 };
			command.Execute(options);

			//Assert
			creatioClient.Received(1).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 1, 2, 3);
		}

		[Test]
		public void PingAppCommandShoulBeUsesAllRetryOptionsOnNet6Environment() {
			PingAppCommand command = _container.Resolve<PingAppCommand>();
			PingAppOptions options = new PingAppOptions() {
				TimeOut = 1,
				RetryCount = 2,
				RetryDelay = 3,
				IsNetCore = true
			};
			command.EnvironmentSettings.IsNetCore = true;
			command.Execute(options);

			//Assert
			creatioClient.Received(1).ExecuteGetRequest(Arg.Any<string>(), 1, 2, 3);
		}
	}
}
