using System;
using System.Linq;
using Clio.Package;

namespace Clio.Command.BusinessRules;

internal interface IBusinessRulePackageResolver {
	Guid ResolveUId(string packageName);
}

internal sealed class BusinessRulePackageResolver(
	IApplicationPackageListProvider applicationPackageListProvider)
	: IBusinessRulePackageResolver {

	public Guid ResolveUId(string packageName) =>
		applicationPackageListProvider.GetPackages()
			.FirstOrDefault(p => string.Equals(p.Descriptor.Name, packageName.Trim(), StringComparison.OrdinalIgnoreCase))
			?.Descriptor.UId
		?? throw new InvalidOperationException($"Package '{packageName}' was not found.");
}
