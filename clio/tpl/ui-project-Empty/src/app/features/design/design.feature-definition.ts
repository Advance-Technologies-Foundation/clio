import type { RemoteFeatureDefinition } from '@creatio-devkit/common';

export const designFeatureDefinition = {
    id: '<%projectName%>-design',
    discovery: {
        viewElements: [],
    },
    activate: () =>
        import('./design.feature-activation').then((m) => m.activateDesignFeature()),
} satisfies RemoteFeatureDefinition;
