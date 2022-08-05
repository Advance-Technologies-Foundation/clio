using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Clio.Dto
{

	internal class EntitySchemaResponse
	{
		[JsonProperty("collection")]
		public List<EntitySchema> Collection { get; set; }
	}

	internal class EntitySchema
	{
		[JsonProperty("isVirtual")]
		public bool Virtual { get; set; }

		[JsonProperty("id")]
		public Guid Id { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("caption")]
		public string Caption { get; set; }

		[JsonProperty("uId")]
		public Guid Uid { get; set; }


		[JsonProperty("packageUId")]
		public Guid PackageUId { get; set; }


		[JsonProperty("parentUId")]
		public Guid ParentUId { get; set; }


		[JsonProperty("extendParent")]
		public bool ExtendsParent { get; set; }
	}
}
