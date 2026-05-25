import type {
  RemoteDesignerDefinitionsLoadContext,
  RemoteFeatureDesignerDefinitions,
} from '@creatio-devkit/common';

/**
 * Loads runtime designer metadata for the remote feature.
 *
 * @param context The designer load context provided by the host application.
 * @returns Runtime designer definitions for desktop and mobile view elements.
 */
export async function loadRuntimeDesignerDefinitions(
  context: RemoteDesignerDefinitionsLoadContext
): Promise<RemoteFeatureDesignerDefinitions> {
  return {
    viewElements: [],
    mobileViewElements: [],
  };
}
