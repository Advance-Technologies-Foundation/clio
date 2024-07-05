using Autofac;
using Clio.Command;
using Clio.Common;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command
{

	[TestFixture(Category = "Unit")]
	internal class PingAppCommandTests : BaseClioModuleTests
	{

		private readonly IApplicationClient _creatioClient = Substitute.For<IApplicationClient>();

		public override void Setup(){}

		protected override void AdditionalRegistrations(ContainerBuilder containerBuilder) {
			base.AdditionalRegistrations(containerBuilder);
			containerBuilder.RegisterInstance(_creatioClient).As<IApplicationClient>();
		}

		[TestCase(true)]
		[TestCase(false)]
		public void PingAppCommandShouldBeUsesAllRetryOptions(bool isNetCore) {
			//Arrange
			_environmentSettings.IsNetCore = isNetCore;
			_fileSystem = CreateFs();
			BindingsModule bindingModule = new(_fileSystem);
			_container = bindingModule.Register(_environmentSettings, true, AdditionalRegistrations);
			
			PingAppCommand command = _container.Resolve<PingAppCommand>();
			PingAppOptions options = new PingAppOptions() { TimeOut = 1, RetryCount = 2, RetryDelay = 3 };
			
			// Act
			command.Execute(options);

			// Assert
			if(isNetCore) {
				_creatioClient.Received(1)
					.ExecuteGetRequest(Arg.Any<string>(), 1, 2, 3);
			}else {
				_creatioClient.Received(1)
					.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), 1, 2, 3);
			}
			_creatioClient.ClearReceivedCalls();
		}

		[TestCase(true)]
		[TestCase(false)]
		public void PingAppCommandShouldBeUsesAllRetryOptionsOnNet6Environment(bool isNetCore) {
			//Arrange
			_fileSystem = CreateFs();
			BindingsModule bindingModule = new(_fileSystem);
			_container = bindingModule.Register(_environmentSettings, true, AdditionalRegistrations);
			PingAppCommand command = _container.Resolve<PingAppCommand>();
			PingAppOptions options = new PingAppOptions() {
				TimeOut = 1,
				RetryCount = 2,
				RetryDelay = 3,
				IsNetCore = isNetCore
			};
			command.EnvironmentSettings.IsNetCore = true;
			
			// Act
			command.Execute(options);

			//Assert
			if(isNetCore) {
				
			}else {
				_creatioClient.Received(1).ExecuteGetRequest(Arg.Any<string>(), 1, 2, 3);
			}
			_creatioClient.ClearReceivedCalls();
		}
	}
}
