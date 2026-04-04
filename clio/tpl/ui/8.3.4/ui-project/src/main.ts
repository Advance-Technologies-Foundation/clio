import type { RemoteEntryDefinition } from '@creatio-devkit/common';
import { designFeatureDefinition } from './app/features/design/design.feature-definition';
import { runtimeFeatureDefinition } from './app/features/runtime/runtime.feature-definition';

export default {
    remoteName: '<%projectName%>',
    features: [runtimeFeatureDefinition, designFeatureDefinition],
} satisfies RemoteEntryDefinition;
