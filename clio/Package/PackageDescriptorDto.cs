using System;
using System.Collections.Generic;

namespace Clio.Package
{
	internal class PackageDescriptorDto
	{
		public class DescriptorDto
		{
			public Guid UId { get; set; }

			public string PackageVersion { get; set; }

			public string Name { get; set; }

			public string ModifiedOnUtc { get; set; }

			public string Maintainer { get; set; }

			public IList<string> DependsOn { get; set; }
		}

		public DescriptorDto Descriptor { get; set; }
	}
}
