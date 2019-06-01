using Terrasoft.Core.Entities;
using Terrasoft.Core.Entities.Events;

namespace <Namespace>
{

	[EntityEventListener(SchemaName = "<EntitySchemaName>")]
	public class <Name>: BaseEntityEventListener
	{

		public override void OnSaved(object sender, EntityAfterEventArgs e) {
			base.OnSaved(sender, e);
			var entity = ((Entity)sender);
			var userConnection = entity.UserConnection;
		}

	}
}