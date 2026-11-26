/**
 * Fixture: the list page of the Classic-to-Freedom benchmark run of 2026-11-26, reduced to its handlers. Names are
 * replaced. The UpdateRecordRequest handler opens the record's own form page instead of the platform's on purpose,
 * and returns next?.handle(request) for every other record.
 * Expected: no handler-without-next finding.
 */
define("UsrDemoV3App_ListPage", /**SCHEMA_DEPS*/["@creatio-devkit/common", "UsrDemoV3App_ListPageModule"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(sdk, listPage)/**SCHEMA_ARGS*/ {
	return {
		viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/,
		viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
		modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
		handlers: /**SCHEMA_HANDLERS*/[
			{
				request: "crt.UpdateRecordRequest",
				handler: async (request, next) => {
					if (listPage.isContractRecordRequest(request)) {
						return listPage.openContractFormPage(request);
					}
					return next?.handle(request);
				}
			},
			{
				request: "crt.HandleViewModelAttributeChangeRequest",
				handler: async (request, next) => {
					const result = await next?.handle(request);
					if (listPage.isSelectionChange(request.attributeName)) {
						await listPage.refreshSelectionState(request);
					}
					return result;
				}
			}
		]/**SCHEMA_HANDLERS*/,
		converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
		validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
	};
});
