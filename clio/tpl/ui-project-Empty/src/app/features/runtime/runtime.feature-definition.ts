import type { RemoteDesignerDefinitionsLoadContext, RemoteFeatureDefinition } from '@creatio-devkit/common';

/**
 * Root definition for the runtime feature.
 *
 * Describes the view elements this remote package contributes at runtime and provides the entry
 * points used to load designer metadata and activate the feature.
 */
export const runtimeFeatureDefinition = {
  id: '<%projectName%>-runtime',
  discovery: {
    viewElements: [],
    mobileViewElements: [],
  },
  loadDesignerDefinitions: (context: RemoteDesignerDefinitionsLoadContext) =>
    import('./runtime.designer-definitions').then((m) => m.loadRuntimeDesignerDefinitions(context)),
  activate: () =>
    import('./runtime.feature-activation').then((m) => m.activateRuntimeFeature()),
} satisfies RemoteFeatureDefinition;
