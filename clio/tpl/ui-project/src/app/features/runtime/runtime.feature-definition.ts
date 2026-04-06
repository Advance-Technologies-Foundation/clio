import type { RemoteDesignerDefinitionsLoadContext, RemoteFeatureDefinition } from '@creatio-devkit/common';
import { DEMO_MOBILE_VIEW_ELEMENT_TYPE, DEMO_VIEW_ELEMENT_TYPE } from './runtime-feature.ids';

export const runtimeFeatureDefinition = {
  id: '<%projectName%>-runtime',
  discovery: {
    viewElements: [
      {
        type: DEMO_VIEW_ELEMENT_TYPE
      }
    ],
    mobileViewElements: [
      {
        type: DEMO_MOBILE_VIEW_ELEMENT_TYPE
      }
    ]
  },
  loadDesignerDefinitions: (context: RemoteDesignerDefinitionsLoadContext) =>
    import('./runtime.designer-definitions').then((m) => m.loadRuntimeDesignerDefinitions(context)),
  activate: () =>
    import('./runtime.feature-activation').then((m) => m.activateRuntimeFeature()),
} satisfies RemoteFeatureDefinition;
