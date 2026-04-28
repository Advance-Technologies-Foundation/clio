import type { RemoteFeatureDefinition } from '@creatio-devkit/common';
import { DEMO_PROPERTY_PANEL_TYPE } from './design-feature.ids';

/**
 * Root definition for the design-time feature.
 *
 * Describes the elements this remote package contributes to the designer, such as property panels,
 * and provides the activation entry point for the feature.
 */
export const designFeatureDefinition = {
  id: '<%projectName%>-design',
  discovery: {
    viewElements: [
      {
        type: DEMO_PROPERTY_PANEL_TYPE
      }
    ],
  },
  activate: () =>
    import('./design.feature-activation').then((m) => m.activateDesignFeature()),
} satisfies RemoteFeatureDefinition;
