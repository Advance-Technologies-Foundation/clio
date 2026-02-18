using System;
using System.Collections.Generic;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
internal class ApplicationPackageListProviderTest : BaseClioModuleTests
{
	[TestCase("[]", 0)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\"}]", 1)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Maintainer\":\"\"}]", 1)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Version\":\"\"}]", 1)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Maintainer\":\"\", \"Version\":\"\"}]", 1)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Maintainer\":\"Y\"}]", 1)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Version\":\"1.0.0\"}]", 1)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Maintainer\":\"Y\", \"Version\":\"1.0.0\"}]", 1)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\" }]", 2)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Maintainer\":\"\" }]", 2)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Version\":\"\" }]", 2)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Maintainer\":\"\", \"Version\":\"\" }]", 2)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Maintainer\":\"Y\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Maintainer\":\"Y\" }]", 2)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Version\":\"1.0.0\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Version\":\"1.1.0\" }]", 2)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Maintainer\":\"Y\", \"Version\":\"1.0.0\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Maintainer\":\"Y\", \"Version\":\"1.1.0\" }]", 2)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000003\" }]", 3)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Maintainer\":\"\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000003\" }]", 3)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Version\":\"\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000003\" }]", 3)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Maintainer\":\"\", \"Version\":\"\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000003\" }]", 3)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Maintainer\":\"Y\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Maintainer\":\"Y\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000003\", \"Maintainer\":\"Y\" }]", 3)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Version\":\"1.0.0\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Version\":\"1.1.0\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000003\", \"Version\":\"2.0.0\" }]", 3)]
	[TestCase("[{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Maintainer\":\"Y\", \"Version\":\"1.0.0\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Maintainer\":\"Y\", \"Version\":\"1.1.0\" },{\"Name\":\"X\", \"UId\":\"00000000-0000-0000-0000-000000000003\", \"Maintainer\":\"Y\", \"Version\":\"2.0.0\" }]", 3)]
	public void CreatePackageInfo_ReturnCorrectPackagesIfResponseCorrect(string responseData, int packageCount){
		//Arrange
		IJsonConverter jsonConverter = Container.Resolve<IJsonConverter>();
		ApplicationPackageListProvider provider = new (jsonConverter);

		//Act
		IEnumerable<PackageInfo> result = provider.ParsePackageInfoResponse(responseData);

		//Assert
		result.Should().HaveCount(packageCount);
	}

	[TestCase("")]
	[TestCase("{}")]
	public void CreatePackageInfo_ThrowExceptionIfResponseIsIncorrect(string responseData){
		//Arrange
		IJsonConverter jsonConverter = Container.Resolve<IJsonConverter>();
		ApplicationPackageListProvider provider = new (jsonConverter);
		Action act = () => provider.ParsePackageInfoResponse(responseData);

		//Act, Assert
		act.Should().Throw<Exception>();
	}
}
