import type {
    RemoteDesignerDefinitionsLoadContext,
    RemoteFeatureDesignerDefinitions,
} from '@creatio-devkit/common';

export async function loadRuntimeDesignerDefinitions(
    context: RemoteDesignerDefinitionsLoadContext,
): Promise<RemoteFeatureDesignerDefinitions> {
    return {
        viewElements: [],
        mobileViewElements: [],
    };
}
