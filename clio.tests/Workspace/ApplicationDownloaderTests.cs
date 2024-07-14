using System;
using System.Collections.Generic;
using System.Linq;
using Autofac;
using Clio.Common;
using Clio.Common.CsProjManager;
using Clio.Tests.Command;
using Clio.WebApplication;
using Clio.Workspaces;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Workspace;

[TestFixture(Category = "Unit")]
public class ApplicationDownloaderTests : BaseClioModuleTests
{

	#region Fields: Private

	private readonly IDownloader _downloaderMock = Substitute.For<IDownloader>();
	private readonly IInitializedCsprojFile _initializedCsprojFile = Substitute.For<IInitializedCsprojFile>();
	private readonly ICsprojFile _csprojFileMock = Substitute.For<ICsprojFile>();
	private readonly IClioGateway _clioGatewayMock = Substitute.For<IClioGateway>();

	private readonly Func<string, string> _mockHintPath = packageName => @"$(RelativePkgFolderPath)/"
		+ packageName +
		@"/$(StandalonePackageAssemblyPath)/"
		+ packageName + ".dll";

	#endregion

	#region Methods: Protected

	protected override void AdditionalRegistrations(ContainerBuilder containerBuilder){
		containerBuilder.RegisterInstance(_csprojFileMock).As<ICsprojFile>();
		containerBuilder.RegisterInstance(_downloaderMock).As<IDownloader>();
		containerBuilder.RegisterInstance(_clioGatewayMock).As<IClioGateway>();
	}

	#endregion

	[Test]
	public void Download_Should_DownloadFiles(){
		// Arrange
		_csprojFileMock.Initialize(Arg.Any<string>()).Returns(_initializedCsprojFile);
		IApplicationDownloader downloader = Container.Resolve<IApplicationDownloader>();
		List<Reference> refs = [
			new Reference("CalendarBase", null, _mockHintPath("CalendarBase"), false, false)
		];
		_initializedCsprojFile.GetPackageReferences()
			.Returns(refs);
		IServiceUrlBuilder serviceUrlBuilder = Container.Resolve<IServiceUrlBuilder>();
		_clioGatewayMock.IsCompatibleWith("2.0.0.29").Returns(true);
		// Act
		downloader.Download(new[] {"CalendarBase"});

		// Assert
		_downloaderMock
			.Received(1)
			.DownloadPackageDll(Arg.Is<IEnumerable<DownloadInfo>>(d =>
				EvalDownloadInfo(d.First(),"CalendarBase", serviceUrlBuilder)));
		_downloaderMock.ClearReceivedCalls();
	}
	
	[Test]
	public void DownloadPackageDll_Skips_When_ClioGateIsOld(){
		// Arrange
		_csprojFileMock.Initialize(Arg.Any<string>()).Returns(_initializedCsprojFile);
		IApplicationDownloader downloader = Container.Resolve<IApplicationDownloader>();
		List<Reference> refs = [
			new Reference("CalendarBase", null, _mockHintPath("CalendarBase"), false, false)
		];
		_initializedCsprojFile.GetPackageReferences()
			.Returns(refs);
		IServiceUrlBuilder serviceUrlBuilder = Container.Resolve<IServiceUrlBuilder>();
		_clioGatewayMock.IsCompatibleWith("2.0.0.29").Returns(false);
		
		// Act
		downloader.Download(new[] {"CalendarBase"});

		// Assert
		_downloaderMock
			.Received(0)
			.DownloadPackageDll(Arg.Is<IEnumerable<DownloadInfo>>(d =>
				EvalDownloadInfo(d.First(),"CalendarBase", serviceUrlBuilder)));
		
		_downloaderMock.ClearReceivedCalls();
	}
	
	[Test]
	public void Download_Skips_WhenPackagesEmpty(){
		// Arrange
		_csprojFileMock.Initialize(Arg.Any<string>()).Returns(_initializedCsprojFile);
		IApplicationDownloader downloader = Container.Resolve<IApplicationDownloader>();
		_initializedCsprojFile.GetPackageReferences().Returns([]);
		_clioGatewayMock.IsCompatibleWith("2.0.0.29").Returns(true);
		
		// Act
		downloader.Download(Array.Empty<string>());

		// Assert
		_downloaderMock
			.Received(0)
			.DownloadPackageDll(Arg.Any<IEnumerable<DownloadInfo>>());
		_downloaderMock.ClearReceivedCalls();
	}
	
	
	
	
	private static bool EvalDownloadInfo(DownloadInfo downloadInfo, string packageName, IServiceUrlBuilder serviceUrlBuilder){
		bool t1 = downloadInfo.Url == serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DownloadPackageDllFile);
		bool t2 = $"{packageName}.dll" == downloadInfo.ArchiveName;
		return t1 && t2;
	}

}