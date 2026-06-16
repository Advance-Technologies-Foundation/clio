using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Category("Unit")]
[TestFixture]
[Property("Module", "Command")]
internal class SetFileContentStorageConnectionStringCommandTests
	: BaseCommandTests<SetFileContentStorageConnectionStringOptions> {

	#region Fields: Private

	private IApplicationClient _applicationClientMock;
	private EnvironmentSettings _envSettingsMock;
	private IServiceUrlBuilder _serviceUrlBuilderMock;
	private SetFileContentStorageConnectionStringCommand _sut;

	#endregion

	#region Methods: Public

	[SetUp]
	public void SetUp(){
		_applicationClientMock = Substitute.For<IApplicationClient>();
		_envSettingsMock = Substitute.For<EnvironmentSettings>();
		_serviceUrlBuilderMock = Substitute.For<IServiceUrlBuilder>();
		_serviceUrlBuilderMock
			.Build(ServiceUrlBuilder.KnownRoute.Update)
			.Returns("http://host/0/DataService/json/SyncReply/UpdateQuery");
		_sut = new SetFileContentStorageConnectionStringCommand(_applicationClientMock, _envSettingsMock,
			_serviceUrlBuilderMock);
	}

	[Test]
	public void Execute_PostsUpdateQueryToDataService_WithCorrectPayload(){
		//Arrange
		var options = new SetFileContentStorageConnectionStringOptions {
			Code = "S3_storage",
			ConnectionString = "ServiceUrl=https://s3.example.com;AccessKey=ak;SecretKey=sk;"
		};
		_applicationClientMock
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"rowsAffected\":1,\"success\":true}");

		//Act
		int result = _sut.Execute(options);

		//Assert
		result.Should().Be(0);
		_applicationClientMock.Received(1).ExecutePostRequest(
			Arg.Is<string>(url => url.Contains("UpdateQuery")),
			Arg.Is<string>(body => body.Contains("SysFileContentStorage")
				&& body.Contains("S3_storage")
				&& body.Contains("ConnectionString")),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	public void Execute_ReturnsError_WhenDataServiceReportsFailure(){
		//Arrange
		var options = new SetFileContentStorageConnectionStringOptions {
			Code = "S3_storage",
			ConnectionString = "ServiceUrl=https://s3.example.com;"
		};
		_applicationClientMock
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"rowsAffected\":0,\"success\":true}");

		//Act
		int result = _sut.Execute(options);

		//Assert
		result.Should().Be(1);
	}

	[Test]
	public void Execute_ReturnsError_WhenStorageCodeNotFound(){
		//Arrange
		var options = new SetFileContentStorageConnectionStringOptions {
			Code = "NonExistentStorage",
			ConnectionString = "ServiceUrl=https://s3.example.com;"
		};
		_applicationClientMock
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"rowsAffected\":0,\"success\":true}");

		//Act
		int result = _sut.Execute(options);

		//Assert
		result.Should().Be(1);
	}

	#endregion

}
