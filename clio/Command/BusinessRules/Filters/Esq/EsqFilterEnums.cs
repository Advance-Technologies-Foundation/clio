namespace Clio.Command.BusinessRules.Filters.Esq;

/// <summary>
/// Numeric values mirror Terrasoft.Nui.ServiceModel.DataContract.FilterType for wire compatibility.
/// </summary>
internal enum EsqFilterType {
	CompareFilter = 1,
	IsNullFilter = 2,
	InFilter = 4,
	Exists = 5,
	FilterGroup = 6
}

/// <summary>
/// Numeric values mirror Terrasoft.Nui.ServiceModel.DataContract.FilterComparisonType.
/// </summary>
internal enum EsqComparisonType {
	IsNotNull = 0,
	IsNull = 1,
	Equal = 3,
	NotEqual = 4,
	Less = 5,
	LessOrEqual = 6,
	Greater = 7,
	GreaterOrEqual = 8,
	StartWith = 9,
	NotStartWith = 10,
	Contain = 11,
	NotContain = 12,
	EndWith = 13,
	NotEndWith = 14,
	Exists = 15,
	NotExists = 16
}

/// <summary>
/// Numeric values mirror Terrasoft.Common.LogicalOperationStrict. Note: this is different from the outer
/// BusinessRuleGroupCondition.logicalOperation which uses 1=AND/2=OR.
/// </summary>
internal enum EsqLogicalOperation {
	And = 0,
	Or = 1
}

/// <summary>
/// Numeric values mirror Terrasoft.Core.Entities.EntitySchemaQueryExpressionType.
/// </summary>
internal enum EsqExpressionType {
	SchemaColumn = 0,
	Parameter = 2
}

/// <summary>
/// Numeric values mirror Terrasoft.Common.DataValueType for the subset used by static-filter values.
/// </summary>
internal enum EsqDataValueType {
	Guid = 0,
	Text = 1,
	Integer = 4,
	Float = 5,
	Money = 6,
	DateTime = 7,
	Date = 8,
	Time = 9,
	Lookup = 10,
	Boolean = 12,
	MediumText = 28
}
