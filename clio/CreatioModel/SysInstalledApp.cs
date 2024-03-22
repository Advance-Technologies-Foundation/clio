#pragma warning disable CS8618, // Non-nullable field is uninitialized.

using System;
using ATF.Repository;
using ATF.Repository.Attributes;
using System.Diagnostics.CodeAnalysis;
using YamlDotNet.Serialization;

namespace CreatioModel
{

	[ExcludeFromCodeCoverage]
	[Schema("SysInstalledApp")]
	public class SysInstalledApp : BaseModel
	{

		[YamlMember(Alias = "name")]
		[SchemaProperty("Name")]
		public string Name { get; set; }

		[YamlMember(Alias = "code")]
		[SchemaProperty("Code")]
		public string Code { get; set; }


		[SchemaProperty("Description")]
		public string Description { get; set; }

		private string _version;
		[YamlMember(Alias = "version")]
		[SchemaProperty("Version")]
		public string Version {
			get { 
				return string.IsNullOrEmpty(_version) ? "none" : _version;
			}
			set { 
				_version = value;
			}
		}

		public override string ToString() {
			return $"\"Id: {Id}, Name: {Name}, Code: {Code}\"";
		}

		[YamlMember(Alias = "apphub")]
		public string AppHubName { get; set; }


		public string ZipFileName
		{
			get;
			internal set;
		}
	}

	[ExcludeFromCodeCoverage]
	[Schema("Contact")]
	public class Contact : BaseModel
	{

		[SchemaProperty("Name")]
		public string Name {
			get; set;
		}

	}

}

#pragma warning restore CS8618 // Non-nullable field is uninitialized.
