import type { RemoteEntryDefinition } from '@creatio-devkit/common';
import { designFeatureDefinition } from './app/features/design/design.feature-definition';
import { runtimeFeatureDefinition } from './app/features/runtime/runtime.feature-definition';
import { REMOTE_ENTRY_NAME } from './app/remote-entry.ids';

export default {
  remoteName: REMOTE_ENTRY_NAME,
  features: [runtimeFeatureDefinition, designFeatureDefinition],
} satisfies RemoteEntryDefinition;
