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
	IsNull = 1,
	IsNotNull = 2,
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
	Function = 1,
	Parameter = 2,
	SubQuery = 3,
	ArithmeticOperation = 4
}

/// <summary>
/// Numeric values mirror Terrasoft.Common.AggregationType (verified by reflection against Terrasoft.Common.dll).
/// </summary>
internal enum EsqAggregationType {
	None = 0,
	Count = 1,
	Sum = 2,
	Avg = 3,
	Min = 4,
	Max = 5
}

/// <summary>
/// Numeric values mirror Terrasoft devkit ɵFunctionType. Only Macros is emitted by static filters.
/// </summary>
internal enum EsqFunctionType {
	None = 0,
	Macros = 1,
	Aggregation = 2,
	DatePart = 3,
	Length = 4,
	Window = 5,
	DateAdd = 6,
	DateDiff = 7
}

/// <summary>
/// Numeric values mirror Terrasoft devkit ɵQueryMacrosType (full set).
/// </summary>
internal enum EsqQueryMacrosType {
	None = 0,
	CurrentUser = 1,
	CurrentUserContact = 2,
	Yesterday = 3,
	Today = 4,
	Tomorrow = 5,
	PreviousWeek = 6,
	CurrentWeek = 7,
	NextWeek = 8,
	PreviousMonth = 9,
	CurrentMonth = 10,
	NextMonth = 11,
	PreviousQuarter = 12,
	CurrentQuarter = 13,
	NextQuarter = 14,
	PreviousHalfYear = 15,
	CurrentHalfYear = 16,
	NextHalfYear = 17,
	PreviousYear = 18,
	CurrentYear = 19,
	PreviousHour = 20,
	CurrentHour = 21,
	NextHour = 22,
	NextYear = 23,
	NextNDays = 24,
	PreviousNDays = 25,
	NextNHours = 26,
	PreviousNHours = 27,
	PrimaryColumn = 34,
	PrimaryDisplayColumn = 35,
	PrimaryImageColumn = 36,
	DayOfYearToday = 37,
	DayOfYearTodayPlusDaysOffset = 38,
	NextNDaysOfYear = 39,
	PreviousNDaysOfYear = 40,
	PrimaryColorColumn = 41
}

/// <summary>
/// Numeric values mirror Terrasoft devkit ɵDatePartType. Used as the <c>datePartType</c> on a
/// DatePart (functionType=3) left-expression that extracts a calendar/clock part from a Date/DateTime/Time column.
/// </summary>
internal enum EsqDatePartType {
	None = 0,
	Day = 1,
	Week = 2,
	Month = 3,
	Year = 4,
	Weekday = 5,
	Hour = 6,
	HourMinute = 7
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
