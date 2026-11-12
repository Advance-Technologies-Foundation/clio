using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Clio.Command.BusinessRules;

internal interface IPageBusinessRuleElementProvider {
	IReadOnlySet<string> GetElementNames(PageBundleInfo bundle);
}

internal sealed class PageBusinessRuleElementProvider : IPageBusinessRuleElementProvider {

	public IReadOnlySet<string> GetElementNames(PageBundleInfo bundle) {
		ArgumentNullException.ThrowIfNull(bundle);

		HashSet<string> result = new(StringComparer.Ordinal);
		foreach ((_, string name, _) in ViewConfigTraversal.EnumerateNamedElements(bundle.ViewConfig)) {
			result.Add(name);
		}
		return result;
	}
}
