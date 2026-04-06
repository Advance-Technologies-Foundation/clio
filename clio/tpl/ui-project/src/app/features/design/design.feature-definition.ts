import type { RemoteFeatureDefinition } from '@creatio-devkit/common';
import { DEMO_PROPERTY_PANEL_TYPE } from './design-feature.ids';

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
