using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Command.CreatioInstallCommand;
using Clio.Common.K8;
using Clio.Tests.Command;
using FluentAssertions;
using k8s;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests;

internal class CreatioInstallerServiceTests : BaseClioModuleTests
{
    #region Fields: Private

    private CreatioInstallerService _creatioInstallerService;

    #endregion

    #region Methods: Protected

    protected override MockFileSystem CreateFs()
    {
        return new MockFileSystem(new Dictionary<string, MockFileData>
        {
            {
                Path.Combine(_remoteArtifactServerPath, "8.1.2", "8.1.2.3888",
                    "BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU",
                    "8.1.2.3888_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_remoteArtifactServerPath, "8.1.2", "8.1.2.3888",
                    "SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU",
                    "8.1.2.3888_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_MSSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_remoteArtifactServerPath, "8.1.2", "8.1.2.3888", "Studio_Softkey_ENU",
                    "8.1.2.3888_Studio_Softkey_MSSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_remoteArtifactServerPath, "8.0.0", "8.0.0.0000",
                    "BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU",
                    "8.0.0.0000_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_remoteArtifactServerPath, "8.0.0", "8.0.0.0000",
                    "SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU",
                    "8.0.0.0000_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_MSSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_remoteArtifactServerPath, "8.0.0", "8.0.0.0000", "Studio_Softkey_ENU",
                    "8.0.0.0000_Studio_Softkey_MSSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
                    "BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU",
                    "8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
                    "SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU",
                    "8.1.3.3992_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_MSSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992", "Studio_Softkey_ENU",
                    "8.1.3.3992_Studio_Softkey_MSSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
                    "BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU",
                    "8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992", "Studio_Softkey_ENU",
                    "8.1.3.3992_Studio_Softkey_PostgreSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_localArtifactServerPath, "8.1.1", "8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_localArtifactServerPath, "8.1.1",
                    "8.1.1.1425_SalesEnterpriseNet6_Softkey_PostgreSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
                    "SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU",
                    "8.1.3.3992_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_PostgreSQL_ENU.zip"),
                new MockFileData("")
            },
            {
                Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3923",
                    "SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU",
                    "8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip"),
                new MockFileData("")
            }
        });
    }

    #endregion

    [Test]
    [Category("Unit")]
    public void FindZipFilePathFromOptionsRemoteServer() {
        //Arrange
        const string product = "BankSales_BankCustomerJourney_Lending_Marketing";

        //Act
        var filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, product,
            CreatioDBType.MSSQL, CreatioRuntimePlatform.NETFramework);

        var expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
            "BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU",
            "8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip");
        
        //Assert
        filePath.Should().Be(expected,
            "because the method should construct the correct path for the specified product, database type, and runtime platform.");
    }

    [Test]
    [Category("Unit")]
    [Description("Should construct the correct path for the specified product, database type, and runtime platform.")]
    public void FindZipFilePathFromOptionsRemoteServer_bcj()
    {
        //Arrange
        PfInstallerOptions options = new() {
            Product = "bcj"
        };

        //Act
        var filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, options.Product,
            CreatioDBType.MSSQL, CreatioRuntimePlatform.NETFramework);

        var expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
            "BankSales_BankCustomerJourney_Lending_Marketing_Softkey_ENU",
            "8.1.3.3992_BankSales_BankCustomerJourney_Lending_Marketing_Softkey_MSSQL_ENU.zip");

        //Assert
        filePath.Should().Be(expected,
            because: "because the method should construct the correct path for the specified product, database type, and runtime platform.");
    }

    [Test]
    [Category("Unit")]
    [Description("Should construct the correct path for Studio product, MSSQL, .NET Framework.")]
    public void FindZipFilePathFromOptionsRemoteServer_MSSQL_NF_S()
    {
        //Arrange
        PfInstallerOptions options = new() {
            Product = "s"
        };

        //Act
        var filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, options.Product,
            CreatioDBType.MSSQL, CreatioRuntimePlatform.NETFramework);

        var expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992", "Studio_Softkey_ENU",
            "8.1.3.3992_Studio_Softkey_MSSQL_ENU.zip");

        //Assert
        filePath.Should()
            .Be(expected,
                "because the method should construct the correct path for Studio product, MSSQL, .NET Framework.");
    }

    [Test]
    [Category("Unit")]
    public void FindZipFilePathFromOptionsRemoteServer_MSSQL_NF_Studio()
    {
        //Arrange
        const string product = "studio";

        //Act
        var filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, product,
            CreatioDBType.MSSQL, CreatioRuntimePlatform.NETFramework);

        var expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992", "Studio_Softkey_ENU",
            "8.1.3.3992_Studio_Softkey_MSSQL_ENU.zip");
        //Assert
        filePath.Should().Be(expected,
                because:"because the method should construct the correct path for Studio product, MSSQL, .NET Framework.");
    }

    [Test]
    [Category("Unit")]
    public void FindZipFilePathFromOptionsRemoteServer_PG_NF_S()
    {
        //Arrange
        PfInstallerOptions options = new() {
            Product = "s"
        };

        //Act
        var filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, options.Product,
            CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NETFramework);

        var expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992", "Studio_Softkey_ENU",
            "8.1.3.3992_Studio_Softkey_PostgreSQL_ENU.zip");
        //Assert
        filePath.Should().Be(expected,
            "because the method should construct the correct path for PostgreSQL, .NET Framework, Studio product from remote artifact server.");
    }

    [Test]
    [Category("Unit")]
    [Description(
        "Should return correct zip file path for PostgreSQL, .NET Framework, Studio product from local artifact server.")]
    public void FindZipFilePathFromOptionsRemoteServer_PG_NF_S_Local()
    {
        //Arrange
        PfInstallerOptions options = new() {
            Product = "s"
        };

        //Act
        var filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_localArtifactServerPath, options.Product,
            CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NETFramework);

        var expected = Path.Combine(_localArtifactServerPath, "8.1.1", "8.1.1.1417_Studio_Softkey_PostgreSQL_ENU.zip");
        //Assert
        filePath.Should().Be(expected,
            "because the method should construct the correct path for PostgreSQL, .NET Framework, Studio product from local artifact server");
    }

    [Test]
    [Category("Unit")]
    public void FindZipFilePathFromOptionsRemoteServer_PG_NF_SE_Local()
    {
        //Arrange
        PfInstallerOptions options = new() {
            Product = "SalesEnterprise"
        };

        var filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_localArtifactServerPath, options.Product,
            CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NET6);

        //Assert
        var expectedPath = Path.Combine(_localArtifactServerPath, "8.1.1",
            "8.1.1.1425_SalesEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");


        filePath.Should().Be(expectedPath,
            "because the method should construct the correct path for SalesEnterprise product, PostgreSQL, .NET 6 from local artifact server.");
    }

    [Test]
    [Category("Unit")]
    public void FindZipFilePathFromOptionsRemoteServer_PG_NF_SE_M_SE()
    {
        //Arrange
        const string product = "SalesEnterprise_Marketing_ServiceEnterprise";

        //Act
        var filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, product,
            CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NETFramework);

        var expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3992",
            "SalesEnterprise_Marketing_ServiceEnterprise_Softkey_ENU",
            "8.1.3.3992_SalesEnterprise_Marketing_ServiceEnterprise_Softkey_PostgreSQL_ENU.zip");
        //Assert
        filePath.Should()
            .Be(expected,
                "because the method should construct the correct path for SalesEnterprise_Marketing_ServiceEnterprise product, PostgreSQL, .NET Framework.");
    }

    [Test]
    [Category("Unit")]
    public void FindZipFilePathFromOptionsRemoteServerNet6Studio()
    {
        //Arrange
        const string product = "SalesEnterprise_Marketing_ServiceEnterprise";

        //Act
        var filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, product,
            CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NET6);

        var expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3923",
            "SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU",
            "8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");

        //Assert
        filePath.Should().Be(expected,
            "because the method should construct the correct path for SalesEnterprise_Marketing_ServiceEnterpriseNet6 product, PostgreSQL, .NET 6.");
    }

    [Test]
    [Category("Unit")]
    public void FindZipFilePathFromOptionsRemoteServerNet6Studio_semse()
    {
        //Arrange
        PfInstallerOptions options = new() {
            Product = "semse"
        };

        //Act
        var filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, options.Product,
            CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NET6);

        //Assert
        var expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3923",
            "SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU",
            "8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");
        filePath.Should().Be(expected,
            "because the method should construct the correct path for semse product, PostgreSQL, .NET 6.");
    }

    [Test]
    public void GetBuildFilePathFromOptions_Returns_Expected()
    {
        //Arrange
        PfInstallerOptions options = new() {
            Product = "semse"
        };

        //Act
        var filePath = _creatioInstallerService.GetBuildFilePathFromOptions(_remoteArtifactServerPath, options.Product,
            CreatioDBType.PostgreSQL, CreatioRuntimePlatform.NET6);

        //Assert
        var expected = Path.Combine(_remoteArtifactServerPath, "8.1.3", "8.1.3.3923",
            "SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_ENU",
            "8.1.3.3923_SalesEnterprise_Marketing_ServiceEnterpriseNet6_Softkey_PostgreSQL_ENU.zip");
        filePath.Should().Be(expected,
            "because the method should construct the correct path for semse product, PostgreSQL, .NET 6.");
    }

    [Test]
    public void GetLatestVersion_Return_Version()
    {
        //Act
        var actual = _creatioInstallerService.GetLatestVersion(_remoteArtifactServerPath);

        //Assert
        actual.Should().Be(Version.Parse("8.1.3"), "because the latest version in the mock file system is 8.1.3.");
    }

    #region Constants: Private

    private readonly string _remoteArtifactServerPath = Environment.OSVersion.Platform == PlatformID.Win32NT
        ? @"\\tscrm.com\dfs-ts\builds-7"
        : "/mnt/tscrm.com/dfs-ts/builds-7";

    private readonly string _localArtifactServerPath = Environment.OSVersion.Platform == PlatformID.Win32NT
        ? @"D:\Projects\creatio_builds"
        : "/usr/usrA/creatio_builds";

    #endregion

    #region Methods: Public

    protected override void AdditionalRegistrations(IServiceCollection containerBuilder)
    {
        base.AdditionalRegistrations(containerBuilder);
        var kuber = Substitute.For<IKubernetes>();
        containerBuilder.AddSingleton(kuber);

        var k8Commands = Substitute.For<Ik8Commands>();
        containerBuilder.AddSingleton(k8Commands);
    }

    public override void Setup()
    {
        base.Setup();
        _creatioInstallerService = Container.GetRequiredService<CreatioInstallerService>();
    }

    #endregion
}