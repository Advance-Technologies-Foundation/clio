using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using clioAgent.ChainOfResponsibility;
using clioAgent.Handlers.ChainLinks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace clioAgent.Tests.Handlers.ChainLinks;

[TestFixture]
public class TemplateLinkTestFixture {

	private readonly MockFileSystem _fileSystem;
	private readonly IServiceProvider _provider;
	TemplateLink _sut;
	public TemplateLinkTestFixture(){
		_fileSystem = new MockFileSystem();
		MockDriveData mockDriveData = new()  {
			DriveType = DriveType.Fixed
		};
		_fileSystem.AddDrive("T", mockDriveData);
		_provider = ConfigureDi();
	}
	
	
	[SetUp]
	public void SetUp(){
		_sut = _provider.GetService<TemplateLink>();
	}
	private IServiceProvider ConfigureDi(){
		ServiceCollection collection = new();
		collection.AddSingleton<IFileSystem>(_fileSystem);
		collection.AddChainOfResponsibility();
		collection.AddChainLink<CopyFileLink, RequestContext, ResponseContext>();
		collection.AddChainLink<UnzipLink, RequestContext, ResponseContext>();
		collection.AddChainLink<CreatePgDbPgLink, RequestContext, ResponseContext>();
		collection.AddChainLink<ResorePgLink, RequestContext, ResponseContext>();
		collection.AddChainLink<ResoreMsLink, RequestContext, ResponseContext>();
		collection.AddChainLink<TemplateLink, RequestContext, ResponseContext>();
		
		collection.AddChain<RequestContext, ResponseContext>((sp, chainBuilder) => {
			chainBuilder
				.AddLink(sp.GetRequiredService<TemplateLink>());
		});
		return collection.BuildServiceProvider();
	}
	
	[TestCase("8.2.3.706_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_MSSQL_ENU.zip", "8.2.3.706_SE_M_SE_Softke_NF")]
	[TestCase("8.2.2.1867_SalesEnterpriseNet8_Softkey_PostgreSQL_ENU.zip", "8.2.2.1867_SE_M_SE_Softke_NC")]
	public async Task Test(string fileName, string expectedTemplateName){
		//Arrange
		string filePath = Path.Join("T", fileName);
		_fileSystem.AddFile(filePath, new MockFileData(string.Empty));
		RequestContext request = new (filePath, string.Empty, false, new ActivityContext());
		var chain = _provider.GetRequiredService<IChainHandler<RequestContext, ResponseContext>>();

		//Act
		var result = await chain.HandleAsync(request);

		//Assert
		result.TemplateName.Should().Be(expectedTemplateName);
	}

}