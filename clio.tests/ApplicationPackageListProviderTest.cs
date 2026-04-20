using System;
using System.Collections.Generic;
using System.Linq;
using ATF.Repository.Mock;
using Clio.Package;
using CreatioModel;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture(Category = "Unit")]
[Property("Module", "Core")]
internal class ApplicationPackageListProviderTest
{
	private MemoryDataProviderMock _dataProvider;
	private ApplicationPackageListProvider _sut;

	[SetUp]
	public void Setup() {
		_dataProvider = new MemoryDataProviderMock();
		_dataProvider.DataStore.RegisterModelSchema<SysPackage>();
		_sut = new ApplicationPackageListProvider(_dataProvider);
	}

	[Test]
	[Description("GetPackages returns empty list when no packages in data store")]
	public void GetPackages_ReturnsEmpty_WhenNoData() {
		IEnumerable<PackageInfo> result = _sut.GetPackages();

		result.Should().BeEmpty("because no packages were seeded");
	}

	[Test]
	[Description("GetPackages returns single package with correct name and UId")]
	public void GetPackages_ReturnsSinglePackage_WhenOneRecord() {
		Guid uid = Guid.Parse("00000000-0000-0000-0000-000000000001");
		_dataProvider.DataStore.AddModel<SysPackage>(p => {
			p.Name = "TestPackage";
			p.UId = uid;
		});

		List<PackageInfo> result = _sut.GetPackages().ToList();

		result.Should().HaveCount(1, "because one package was seeded");
		result[0].Descriptor.Name.Should().Be("TestPackage");
		result[0].Descriptor.UId.Should().Be(uid);
	}

	[Test]
	[Description("GetPackages correctly maps Maintainer and Version fields")]
	public void GetPackages_MapsAllFields_WhenFullRecord() {
		Guid uid = Guid.Parse("a0120c05-78fd-41e4-baf5-112ab9006c3e");
		_dataProvider.DataStore.AddModel<SysPackage>(p => {
			p.Name = "MyPkg";
			p.UId = uid;
			p.Maintainer = "Creatio";
			p.Version = "1.2.3";
		});

		List<PackageInfo> result = _sut.GetPackages().ToList();

		result.Should().HaveCount(1);
		result[0].Descriptor.Maintainer.Should().Be("Creatio");
		result[0].Descriptor.PackageVersion.Should().Be("1.2.3");
	}

	[Test]
	[Description("GetPackages returns multiple packages when multiple records exist")]
	public void GetPackages_ReturnsMultiple_WhenMultipleRecords() {
		_dataProvider.DataStore.AddModel<SysPackage>(p => p.Name = "Pkg1");
		_dataProvider.DataStore.AddModel<SysPackage>(p => p.Name = "Pkg2");
		_dataProvider.DataStore.AddModel<SysPackage>(p => p.Name = "Pkg3");

		List<PackageInfo> result = _sut.GetPackages().ToList();

		result.Should().HaveCount(3, "because three packages were seeded");
	}

	[Test]
	[Description("GetPackages with isCustomer filter returns only InstallType=0 packages")]
	public void GetPackages_WithCustomerFilter_ReturnsOnlyCustomerPackages() {
		_dataProvider.DataStore.AddModel<SysPackage>(p => {
			p.Name = "CustomerPkg";
			p.InstallType = 0;
		});
		_dataProvider.DataStore.AddModel<SysPackage>(p => {
			p.Name = "SystemPkg";
			p.InstallType = 1;
		});

		List<PackageInfo> result = _sut.GetPackages("{\"isCustomer\": true}").ToList();

		result.Should().HaveCount(1, "because only one package has InstallType=0");
		result[0].Descriptor.Name.Should().Be("CustomerPkg");
	}

	[Test]
	[Description("GetPackages with isCustomer as string 'true' also filters correctly")]
	public void GetPackages_WithCustomerFilterAsString_FiltersCorrectly() {
		_dataProvider.DataStore.AddModel<SysPackage>(p => {
			p.Name = "CustomerPkg";
			p.InstallType = 0;
		});
		_dataProvider.DataStore.AddModel<SysPackage>(p => {
			p.Name = "SystemPkg";
			p.InstallType = 1;
		});

		List<PackageInfo> result = _sut.GetPackages("{\"isCustomer\": \"true\"}").ToList();

		result.Should().HaveCount(1, "because string 'true' is treated as boolean true");
		result[0].Descriptor.Name.Should().Be("CustomerPkg");
	}

	[Test]
	[Description("GetPackages with empty scriptData returns all packages")]
	public void GetPackages_WithEmptyScriptData_ReturnsAll() {
		_dataProvider.DataStore.AddModel<SysPackage>(p => p.InstallType = 0);
		_dataProvider.DataStore.AddModel<SysPackage>(p => p.InstallType = 1);

		List<PackageInfo> result = _sut.GetPackages("{}").ToList();

		result.Should().HaveCount(2, "because no filter is applied for empty script data");
	}
}
