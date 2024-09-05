using Autofac;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using FluentAssertions;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Clio.Tests
{
	[TestFixture]
	internal class ApplicationPackageListProviderTest: BaseClioModuleTests
	{
		[TestCase("")]
		[TestCase("{}")]
		public void CreatePackageInfo_ThrowExceprionIfResponseIsIncorect(string responseData) {
			IJsonConverter jsonConverter = Container.Resolve<IJsonConverter>();
			var provider = new ApplicationPackageListProvider(jsonConverter);
			Action act = () => provider.ParsePackageInfoResponse(responseData);
			act.Should().Throw<Exception>();
		}

		[TestCase("[]", 0)]
		[TestCase("[{\"Name\":\"X\"}]", 1)]
		[TestCase("[{\"Name\":\"X\", \"Maintainer\":\"Y\" }]", 1)]
		[TestCase("[{\"Name\":\"X\", \"Maintainer\":\"Y\" },{\"Name\":\"X\", \"Maintainer\":\"Y\" }]", 2)]
		[TestCase("[{\"Name\":\"X\", \"Maintainer\":\"Y\" },{\"Name\":\"X\", \"Maintainer\":\"Y\" },{\"Name\":\"X\", \"Maintainer\":\"Y\" }]", 3)]
		[TestCase("[{\"Name\":\"X\", \"Maintainer\":\"Y\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Version\":\"1.0.0\" }]", 1)]
		[TestCase("[{\"Name\":\"X\", \"Maintainer\":\"Y\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Version\":\"1.0.0\" },{\"Name\":\"X\", \"Maintainer\":\"Y\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Version\":\"1.1.0\" }]", 2)]
		[TestCase("[{\"Name\":\"X\", \"Maintainer\":\"Y\", \"UId\":\"00000000-0000-0000-0000-000000000001\", \"Version\":\"1.0.0\" },{\"Name\":\"X\", \"Maintainer\":\"Y\", \"UId\":\"00000000-0000-0000-0000-000000000002\", \"Version\":\"1.1.0\" },{\"Name\":\"X\", \"Maintainer\":\"Y\", \"UId\":\"00000000-0000-0000-0000-000000000003\", \"Version\":\"2.0.0\" }]", 3)]
		public void CreatePackageInfo_ReturnCorrectPackagesIfResponseCorrect(string responseData, int packageCount) {
			IJsonConverter jsonConverter = Container.Resolve<IJsonConverter>();
			var provider = new ApplicationPackageListProvider(jsonConverter);
			var result = provider.ParsePackageInfoResponse(responseData);
			result.Should().HaveCount(packageCount);
		}


	}
}
