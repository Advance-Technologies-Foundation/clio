define("Contacts_FormPage", /**SCHEMA_DEPS*/[]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/()/**SCHEMA_ARGS*/ {
	return {
		viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[
			{
				"operation": "insert",
				"name": "TabContainer_c52wspk",
				"values": {
					"type": "crt.TabContainer",
					"items": [],
					"caption": "#ResourceString(TabContainer_c52wspk_caption)#",
					"iconPosition": "only-text",
					"visible": true
				},
				"parentName": "Tabs",
				"propertyName": "items",
				"index": 5
			},
			{
				"operation": "insert",
				"name": "GridContainer_vz2su0o",
				"values": {
					"type": "crt.GridContainer",
					"items": [],
					"rows": "minmax(32px, max-content)",
					"columns": [
						"minmax(32px, 1fr)",
						"minmax(32px, 1fr)"
					],
					"gap": {
						"columnGap": "large",
						"rowGap": "none"
					},
					"visible": true,
					"color": "transparent",
					"borderRadius": "none",
					"padding": {
						"top": "none",
						"right": "none",
						"bottom": "none",
						"left": "none"
					},
					"alignItems": "stretch"
				},
				"parentName": "TabContainer_c52wspk",
				"propertyName": "items",
				"index": 0
			},
			{
				"operation": "insert",
				"name": "Demo_o1cuwh8",
				"values": {
					"layoutConfig": {
						"column": 1,
						"row": 1,
						"colSpan": 2,
						"rowSpan": 1
					},
					"type": "usr.Demo",
					"iframesrc":"https://quote.us1.proscloud.com/quotex-ui-server/login?tenant_id=c18ff519-f62e-450b-a900-bfbe95e881a9&amp;env=c18ff519-f62e-450b-a900-bfbe95e881a9&amp;environment_id=dev&amp;user_id=clafont@pros.stddemos.com&amp;session_id=64761d9c-ca5d-461a-8c0d-d1513ce8ed8f&amp;quote_id=3181169f-6030-4cc0-9bcb-a8ec598278e2&amp;model_id=QM-58318152624543d4bbbcf140ec3b8982&amp;quote_step_id=CatalogStep&amp;theme_name=&amp;ui_lang=en_US&amp;timezone=GMT-08%253A00&amp;integration_type=SFDC&amp;integration_endpoint=https://pros-stddemos-dev-ed.my.salesforce.com/a0F4P00000Ru6nQUAR&amp;iam=@pros.stddemos.com&amp;standalone=false",
					"title":"quotes"
				},
				"parentName": "GridContainer_vz2su0o",
				"propertyName": "items",
				"index": 0
			}
		]/**SCHEMA_VIEW_CONFIG_DIFF*/,
		viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
		modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
		handlers: /**SCHEMA_HANDLERS*/[]/**SCHEMA_HANDLERS*/,
		converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
		validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
	};
});