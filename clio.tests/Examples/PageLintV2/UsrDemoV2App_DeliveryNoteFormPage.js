define("UsrDemoV2App_DeliveryNoteFormPage", /**SCHEMA_DEPS*/["@creatio-devkit/common", "UsrDemoV2DocumentTotals"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(sdk, demoDocumentTotals)/**SCHEMA_ARGS*/ {
	return {
		viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/,
		viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
		modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
		handlers: /**SCHEMA_HANDLERS*/[
			{
				request: "crt.HandleViewModelAttributeChangeRequest",
				handler: async (request, next) => {
					const result = await next?.handle(request);
					if (request.attributeName === "PDS_DemoIsPosted") {
						await request.$context.set("DemoPostDisabled", true);
					}
					return result;
				}
			},
			{
				request: "usr.DemoPostButtonClicked",
				handler: async (request) => {
					await demoDocumentTotals.postDocument(sdk, request, {
						postedAttributeName: "PDS_DemoIsPosted"
					});
				}
			}
		]/**SCHEMA_HANDLERS*/,
		converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
		validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
	};
});
