namespace Clio.Command.BusinessRules.Filters;

internal static class BusinessRuleFilterErrorCodes {
	internal const string TargetAttributeRequired = "filter.target-attribute-required";
	internal const string TargetAttributeUnknown = "filter.target-attribute-unknown";
	internal const string TargetAttributeNotLookup = "filter.target-attribute-not-lookup";
	internal const string ItemsNotAllowed = "filter.items-not-allowed";
	internal const string FilterRequired = "filter.required";
	internal const string PathUnknown = "filter.path-unknown";
	internal const string PathResolvesToCollection = "filter.path-resolves-to-collection";
	internal const string LogicalOperationUnknown = "filter.logical-operation-unknown";
	internal const string ComparisonUnknown = "filter.comparison-unknown";
	internal const string ComparisonNotSupportedForDatatype = "filter.comparison-not-supported-for-datatype";
	internal const string ValueRequired = "filter.value-required";
	internal const string ValueShape = "filter.value-shape";
	internal const string LookupValueNotGuid = "filter.lookup-value-not-guid";
	internal const string LookupRecordNotFound = "filter.lookup-record-not-found";
	internal const string BackwardReferenceNot1N = "filter.backward-reference-not-1n";
	internal const string ServerRejected = "filter.server-rejected";
}
