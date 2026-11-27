/**
 * Fixture: the record page of the Classic-to-Freedom benchmark run of 2026-11-26, reduced to its handlers. Names are
 * replaced. The UpdateRecordRequest handler stops the chain on purpose when the record opens its own child page, and
 * returns next?.handle(request) on every other path.
 * Expected: no handler-without-next finding.
 */
define("UsrDemoV3App_FormPage", /**SCHEMA_DEPS*/["@creatio-devkit/common", "UsrDemoV3App_FormPageModule"]/**SCHEMA_DEPS*/, function/**SCHEMA_ARGS*/(sdk, contractPage)/**SCHEMA_ARGS*/ {
	return {
		viewConfigDiff: /**SCHEMA_VIEW_CONFIG_DIFF*/[]/**SCHEMA_VIEW_CONFIG_DIFF*/,
		viewModelConfigDiff: /**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/[]/**SCHEMA_VIEW_MODEL_CONFIG_DIFF*/,
		modelConfigDiff: /**SCHEMA_MODEL_CONFIG_DIFF*/[]/**SCHEMA_MODEL_CONFIG_DIFF*/,
		handlers: /**SCHEMA_HANDLERS*/[
			{
				request: "crt.CreateRecordRequest",
				handler: async (request, next) => {
					await contractPage.useChildPageForAdd(request);
					return next?.handle(request);
				}
			},
			{
				request: "crt.UpdateRecordRequest",
				handler: async (request, next) => {
					const childPage = contractPage.childPageOf(request);
					if (childPage) {
						return contractPage.openChildPage(request, childPage);
					}
					return next?.handle(request);
				}
			}
		]/**SCHEMA_HANDLERS*/,
		converters: /**SCHEMA_CONVERTERS*/{}/**SCHEMA_CONVERTERS*/,
		validators: /**SCHEMA_VALIDATORS*/{}/**SCHEMA_VALIDATORS*/
	};
});
