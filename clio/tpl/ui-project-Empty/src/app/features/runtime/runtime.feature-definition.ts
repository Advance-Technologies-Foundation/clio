import type { RemoteDesignerDefinitionsLoadContext, RemoteFeatureDefinition } from '@creatio-devkit/common';

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
