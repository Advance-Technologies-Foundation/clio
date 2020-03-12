using System;
using System.Collections.Generic;

namespace Clio.Package
{
	public class PackageDescriptor
	{
		public Guid UId { get; set; }

		public string PackageVersion { get; set; }

		public string Name { get; set; }

		public string ModifiedOnUtc { get; set; }

		public string Maintainer { get; set; }

		public IList<PackageDependency> DependsOn { get; set; }
	}
}
