using System.Diagnostics.CodeAnalysis;
using ATF.Repository;
using ATF.Repository.Attributes;

namespace CreatioModel;

#pragma warning disable CS8618
[ExcludeFromCodeCoverage]
[Schema("Contact")]
public class Contact : BaseModel
{

	[SchemaProperty("Name")]
	public string Name {
		get; set;
	}

}
