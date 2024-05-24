#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;
using Terrasoft.Core.Configuration;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("VwSysSetting")]
	public class VwSysSetting:BaseModel {

		[SchemaProperty("Name")]
		public string Name { get; set; }

		[SchemaProperty("Description")]
		public string Description { get; set; }

		[SchemaProperty("Code")]
		public string Code { get; set; }

		[SchemaProperty("ValueTypeName")]
		public string ValueTypeName { get; set; }

		[SchemaProperty("ReferenceSchemaUId")]
		public Guid ReferenceSchemaUIdId { get; set; }

		[LookupProperty("ReferenceSchemaUId")]
		public virtual SysSchema ReferenceSchemaUId { get; set; }

		[SchemaProperty("IsPersonal")]
		public bool IsPersonal { get; set; }

		[SchemaProperty("IsCacheable")]
		public bool IsCacheable { get; set; }

		[SchemaProperty("IsSSPAvailable")	]
		public bool IsSSPAvailable { get; set; }

	}


	[ExcludeFromCodeCoverage]
	[Schema("SysSettings")]
	public class SysSettings : BaseModel
	{

		[SchemaProperty("Name")]
		public string Name { get; set; }

		[SchemaProperty("Description")]
		public string Description { get; set; }

		[SchemaProperty("Code")]
		public string Code { get; set; }

		[SchemaProperty("ValueTypeName")]
		public string ValueTypeName { get; set; }

		[SchemaProperty("ReferenceSchemaUId")]
		public Guid ReferenceSchemaUIdId { get; set; }

		// [LookupProperty("ReferenceSchemaUId")]
		// public virtual SysSchema ReferenceSchemaUId { get; set; }

		[SchemaProperty("IsPersonal")]
		public bool IsPersonal { get; set; }

		[SchemaProperty("IsCacheable")]
		public bool IsCacheable { get; set; }

		[SchemaProperty("IsSSPAvailable")]
		public bool IsSSPAvailable { get; set; }

		[DetailProperty("SysSettingsId")]
		public virtual List<SysSettingsValue> SysSettingsValues { get; set; }

		public string DefValue {
			get {
				return GetDefaultValue();
			}
		}

		private Guid AllUsersId = new Guid("a29a3ba5-4b0d-de11-9a51-005056c00008");

		private string GetDefaultValue(string adminUnitName = null) {
			var sysSettingsValue = SysSettingsValues?.Where(x => x.SysAdminUnitId == AllUsersId)?.FirstOrDefault();
			if (sysSettingsValue != null) {
				switch (ValueTypeName) {
					case "Boolean":
						return sysSettingsValue.BooleanValue.ToString().ToLower();
					case "MediumText":
					case "ShortText":
					case "LongText":
					case "Text":
						return sysSettingsValue.TextValue;
					case "Integer":
						return sysSettingsValue.IntegerValue.ToString();
					case "Date":
						return sysSettingsValue.DateTimeValue.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
					case "Time":
						return sysSettingsValue.DateTimeValue.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
					case "Float":
					case "Decimal":
					case "Currency":
						return sysSettingsValue.FloatValue.ToString();
					case "Lookup":
						return sysSettingsValue.GuidValue.ToString();
				}
			}
			return "undefined";
		}
	}

	[ExcludeFromCodeCoverage]
	[Schema("SysSettingsValue")]
	public class SysSettingsValue : BaseModel
	{
		[SchemaProperty("SysSettings")]
		public Guid SysSettingsId { get; set; }

		[LookupProperty("SysSettings")]
		public virtual SysSettings SysSettings { get; set; }

		[SchemaProperty("SysAdminUnit")]
		public Guid SysAdminUnitId { get; set; }

		[LookupProperty("SysAdminUnit")]
		public virtual SysAdminUnit SysAdminUnit { get; set; }

		[SchemaProperty("IsDef")]
		public bool IsDef { get; set; }

		[SchemaProperty("TextValue")]
		public string TextValue { get; set; }

		[SchemaProperty("IntegerValue")]
		public int IntegerValue { get; set; }

		[SchemaProperty("FloatValue")]
		public decimal FloatValue { get; set; }

		[SchemaProperty("BooleanValue")]
		public bool BooleanValue { get; set; }

		[SchemaProperty("DateTimeValue")]
		public DateTime DateTimeValue { get; set; }

		[SchemaProperty("GuidValue")]
		public Guid GuidValue { get; set; }

	}
}
#pragma warning restore CS8618 // Non-nullable field is uninitialized.
