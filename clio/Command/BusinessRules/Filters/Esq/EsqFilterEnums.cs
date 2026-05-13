// Numeric values mirror Terrasoft platform enums so JSON-serialized envelopes are accepted
// by Creatio runtime without any package dependency. Source assemblies for each enum:
//   Terrasoft.Nui.ServiceModel.DataContract: FilterType, FunctionType, DataValueType, DatePart
//   Terrasoft.Core.Entities: FilterComparisonType, EntitySchemaQueryExpressionType,
//                            EntitySchemaQueryMacrosType
//   Terrasoft.Common: LogicalOperationStrict, AggregationType, ArithmeticOperation, OrderDirection
//   Terrasoft.Core.DB: AggregationEvalType, DateDiffQueryFunctionInterval
// Values verified by reflection against Terrasoft.*.dll v8.x.

namespace Clio.Command.BusinessRules.Filters.Esq;

internal enum EsqFilterType {
	None = 0,
	CompareFilter = 1,
	IsNullFilter = 2,
	Between = 3,
	InFilter = 4,
	Exists = 5,
	FilterGroup = 6
}

internal enum EsqFilterComparisonType {
	Between = 0,
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

internal enum EsqLogicalOperationStrict {
	And = 0,
	Or = 1
}

internal enum EsqEntitySchemaQueryExpressionType {
	SchemaColumn = 0,
	Function = 1,
	Parameter = 2,
	SubQuery = 3,
	ArithmeticOperation = 4
}

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
	Enum = 11,
	Boolean = 12,
	Blob = 13,
	Image = 14,
	Object = 15,
	ImageLookup = 16,
	ValueList = 17,
	Color = 18,
	HashText = 23,
	SecureText = 24,
	File = 25,
	Mapping = 26,
	ShortText = 27,
	MediumText = 28,
	MaxSizeText = 29,
	LongText = 30,
	Float1 = 31,
	Float2 = 32,
	Float3 = 33,
	Float4 = 34,
	Float8 = 40,
	FileLocator = 41,
	PhoneText = 42,
	RichText = 43,
	WebText = 44,
	EmailText = 45
}

internal enum EsqAggregationType {
	None = 0,
	Count = 1,
	Sum = 2,
	Avg = 3,
	Min = 4,
	Max = 5,
	TopOne = 6
}

internal enum EsqAggregationEvalType {
	None = 0,
	All = 1,
	Distinct = 2
}

internal enum EsqArithmeticOperation {
	Addition = 0,
	Subtraction = 1,
	Multiplication = 2,
	Division = 3
}

internal enum EsqOrderDirection {
	None = 0,
	Ascending = 1,
	Descending = 2
}

internal enum EsqDatePart {
	None = 0,
	Day = 1,
	Week = 2,
	Month = 3,
	Year = 4,
	Weekday = 5,
	Hour = 6,
	HourMinute = 7
}

internal enum EsqDateDiffQueryFunctionInterval {
	Year = 0,
	Month = 1,
	Day = 2,
	Hour = 3,
	Minute = 4,
	Millisecond = 5
}

internal enum EsqEntitySchemaQueryMacrosType {
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
	DayOfMonth = 28,
	DayOfWeek = 29,
	Hour = 30,
	HourMinute = 31,
	Month = 32,
	Year = 33,
	DayOfYearToday = 37,
	DayOfYearTodayPlusDaysOffset = 38,
	NextNDaysOfYear = 39,
	PreviousNDaysOfYear = 40
}
