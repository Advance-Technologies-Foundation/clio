using System;
using System.IO;
using System.Text;
using System.Threading;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[Author("Kirill Krylov", "k.krylov@creatio.com")]
[Category("UnitTests")]
[TestFixture]
internal class FeatureCommandCommandTests : BaseCommandTests<FeatureOptions> {

	#region Setup/Teardown

	[SetUp]
	public override void Setup(){
		_sb.Clear();
		_textWriter.Flush();
		_sut = new FeatureCommand(_applicationClientMock, _envSettingsMock, _dataProviderMock, _serviceUrlBuilderMock);
	}

	#endregion

	#region Fields: Private

	private FeatureCommand _sut;
	private EnvironmentSettings _envSettingsMock;
	private IDataProvider _dataProviderMock;
	private IServiceUrlBuilder _serviceUrlBuilderMock;
	private IApplicationClient _applicationClientMock;
	private StringWriter _textWriter;
	private StringBuilder _sb;
	private TextWriter _originalTextWriter;

	#endregion

	#region Methods: Public

	[OneTimeSetUp]
	public void OneTimeSetUp(){
		ConsoleLogger.Instance.Start();
		_applicationClientMock = Substitute.For<IApplicationClient>();

		_envSettingsMock = Substitute.For<EnvironmentSettings>();
		_dataProviderMock = Substitute.For<IDataProvider>();
		_serviceUrlBuilderMock = Substitute.For<IServiceUrlBuilder>();

		_sb = new StringBuilder();
		_textWriter = new StringWriter(_sb);
		_originalTextWriter = Console.Out;
		Console.SetOut(_textWriter);
	}

	[OneTimeTearDown]
	public void TearDown(){
		Console.SetOut(_originalTextWriter);
		_sb.Clear();
		_textWriter.Flush();
		_textWriter.Close();
		_textWriter.Dispose();
	}

	#endregion

	[Description("Should clear cache for the given feature name and log the result")]
	[TestCase("ActivateAdvancedModeForOriginalSchemas")]
	public void Execute_ShouldClearCacheAndLogResult(string featureName){
		//Arrange
		string base64FeatureName = Convert.ToBase64String(Encoding.UTF8.GetBytes(featureName));
		string knownUrl = $"http://localhost/0/rest/FeatureService/ClearFeaturesCacheForAllUsers/{base64FeatureName}";

		string responseBody = $"Cache cleared for feature {featureName}";

		_applicationClientMock.ExecuteGetRequest(Arg.Is(knownUrl)).Returns(responseBody);
		_serviceUrlBuilderMock.Build(ServiceUrlBuilder.KnownRoute.ClearFeaturesCacheForAllUsers)
							.Returns("http://localhost/0/rest/FeatureService/ClearFeaturesCacheForAllUsers");

		//Act
		_sut.ClearCache("ActivateAdvancedModeForOriginalSchemas");
		Thread.Sleep(300); //Logger need 300ms to flush its cache, thus we sleep

		//Assert
		_textWriter.ToString().Should().Contain($"[INF] - {responseBody}\r\n");
	}

}