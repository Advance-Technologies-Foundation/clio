define("SystemDesigner", [], function() {
	return {
		methods: {
			/**
			 * Opens Realtime log UI.
			 * @private
			 */
			navigateToRealog: function() {
				const realogPageUrl = Terrasoft.workspaceBaseUrl + "/Nui/ViewModule.aspx#BaseSchemaModuleV2/Realog";
				window.open(realogPageUrl);
			},
			/**
			 * @override
			 */
			getOperationRightsDecoupling: function() {
				const result = this.callParent(arguments);
				result.navigateToRealog = "CanUseLoggerDashboard";
				return result;
			}
		},
		diff: [
			{
				"operation": "insert",
				"propertyName": "items",
				"parentName": "ConfigurationTile",
				"name": "RealogPage",
				"values": {
					"itemType": Terrasoft.ViewItemType.LINK,
					"caption": {"bindTo": "Resources.Strings.RealogCaption"},
					"tag": "navigateToRealog",
					"click": {"bindTo": "invokeOperation"}
				}
			}
		]
	};
});
