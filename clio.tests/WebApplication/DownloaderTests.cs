using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Autofac;
using Clio.Common;
using Clio.Tests.Command;
using Clio.WebApplication;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.WebApplication;

[TestFixture(Category = "Unit")]
public class DownloaderTests : BaseClioModuleTests
{

	#region Fields: Private

	private readonly ILogger _loggerMock = Substitute.For<ILogger>();
	private readonly IApplicationClient _applicationClientMock = Substitute.For<IApplicationClient>();

	private readonly IApplicationClientFactory _applicationClientFactoryMock
		= Substitute.For<IApplicationClientFactory>();

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		containerBuilder.RegisterInstance(_loggerMock).As<ILogger>();
		containerBuilder.RegisterInstance(_applicationClientMock).As<IApplicationClient>();
		containerBuilder.RegisterInstance(_applicationClientFactoryMock).As<IApplicationClientFactory>();
	}

	#endregion

	[Test]
	public void DownloadPackageDll_CopiesDownloadedFile(){
		// Arrange
		IServiceUrlBuilder urlBuilder = Container.Resolve<IServiceUrlBuilder>();
		string url = urlBuilder.Build(ServiceUrlBuilder.KnownRoute.DownloadPackageDllFile);
		const string archiveName = "MyPackage.dll";
		const string mockFileContent = "file content";
		string destinationPath = Path.Combine("T:\\DestFolder", archiveName);
		const string requestData = "my request data";
		DownloadInfo downloadInfo = new DownloadInfo(url, archiveName, destinationPath, requestData);

		string tempDir = Path.Combine("T:\\Clio", Guid.NewGuid().ToString());
		string archiveFilePath = Path.Combine(tempDir, $"{downloadInfo.ArchiveName}");
		_applicationClientMock
			.When(c =>
				c.DownloadFile(url, archiveFilePath, requestData))
			.Do(c => {
				FileSystem.AddFile(Path.Combine(tempDir, archiveName), new MockFileData(mockFileContent));
			});

		_applicationClientFactoryMock.CreateClient(Arg.Any<EnvironmentSettings>())
			.Returns(_applicationClientMock);

		// Act
		(Container.Resolve<IDownloader>() as Downloader)?.DownloadPackageDll(downloadInfo, tempDir);

		// Assert
		string expectedLog = $"Run download - OK: {archiveName}";
		_loggerMock.Received(1).WriteInfo(expectedLog);
		_loggerMock.ClearReceivedCalls();
		FileSystem.File.Exists(destinationPath).Should().BeTrue();
		FileSystem.File.ReadAllText(destinationPath).Should().Be(mockFileContent);
	}

	[Test]
	public void DownloadPackageDll_WritesWarning_When_DownloadedFileIsSizeOfZero(){
		// Arrange
		IServiceUrlBuilder urlBuilder = Container.Resolve<IServiceUrlBuilder>();
		string url = urlBuilder.Build(ServiceUrlBuilder.KnownRoute.DownloadPackageDllFile);
		const string archiveName = "MyPackage.dll";
		DownloadInfo downloadInfo = new DownloadInfo(url, archiveName, "", "");
		string tempDir = Path.Combine("C:\\", Guid.NewGuid().ToString());
		string destinationPath = Path.Combine(tempDir, archiveName);
		_applicationClientMock
			.When(c =>
				c.DownloadFile(url, Arg.Any<string>(), Arg.Any<string>()))
			.Do(c => {
				FileSystem.RemoveFile(destinationPath);
				FileSystem.AddEmptyFile(destinationPath);
			});

		_applicationClientFactoryMock.CreateClient(Arg.Any<EnvironmentSettings>())
			.Returns(_applicationClientMock);

		// Act
		(Container.Resolve<IDownloader>() as Downloader)?.DownloadPackageDll(downloadInfo, tempDir);

		// Assert

		string expectedLog = $"File: {archiveName} is empty";
		_loggerMock.Received(1)
			.WriteWarning(expectedLog);
		_loggerMock.ClearReceivedCalls();
	}

	[Test]
	public void DownloadPackageDll_WritesWarning_When_UrlInvalid(){
		// Arrange
		const string url = "my_url";
		const string archiveName = "MyPackage.dll";
		IDownloader downloader = Container.Resolve<IDownloader>();

		string warningMessage = $@"Invalid URI: The format of the URI could not be determined.{Environment.NewLine}";
		_applicationClientMock
			.When(c =>
				c.DownloadFile(url, Arg.Any<string>(), Arg.Any<string>()))
			.Do(c => throw new Exception(warningMessage));
		_applicationClientFactoryMock.CreateClient(Arg.Any<EnvironmentSettings>())
			.Returns(_applicationClientMock);

		IEnumerable<DownloadInfo> downloadInfos = [
			new DownloadInfo(url, archiveName, "", "")
		];

		// Act
		downloader.DownloadPackageDll(downloadInfos);

		// Assert
		_loggerMock.Received(1)
			.WriteWarning(warningMessage + Environment.NewLine);
		_loggerMock.ClearReceivedCalls();
	}

	[Test]
	public void DownloadPackageDll_WritesWarning_WhenDownloadedFileNotFound(){
		// Arrange
		IServiceUrlBuilder urlBuilder = Container.Resolve<IServiceUrlBuilder>();
		string url = urlBuilder.Build(ServiceUrlBuilder.KnownRoute.DownloadPackageDllFile);
		const string archiveName = "MyPackage.dll";
		DownloadInfo downloadInfo = new DownloadInfo(url, archiveName, "", "");
		string tempDir = Path.Combine("C:\\", Guid.NewGuid().ToString());

		_applicationClientFactoryMock.CreateClient(Arg.Any<EnvironmentSettings>())
			.Returns(_applicationClientMock);

		_applicationClientMock
			.When(c =>
				c.DownloadFile(url, Arg.Any<string>(), Arg.Any<string>()));

		// Act
		(Container.Resolve<IDownloader>() as Downloader)?.DownloadPackageDll(downloadInfo, tempDir);

		// Assert

		string expectedLog = $"Could not find file '{Path.Combine(tempDir, archiveName)}'.";
		_loggerMock.Received(1)
			.WriteWarning(expectedLog);
		_loggerMock.ClearReceivedCalls();
	}

	[Test]
	public void DownloadPackageDll_WriteWarning_When_packageNameEmpty(){
		// Arrange
		const string url = "my_url";
		IDownloader downloader = Container.Resolve<IDownloader>();
		IEnumerable<DownloadInfo> downloadInfos = [
			new DownloadInfo(url, "", "", "")
		];

		// Act
		downloader.DownloadPackageDll(downloadInfos);

		// Assert
		_loggerMock.Received(1)
			.WriteWarning($@"Packages name is empty, skip download {url}");
		_loggerMock.ClearReceivedCalls();
	}

}