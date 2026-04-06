import { ProviderToken } from '@angular/core';
import { bootstrapCrtModule } from '@creatio-devkit/common';
import { RuntimeFeatureModule } from './runtime-feature.module';
import { ensureFeatureModuleRef } from '../../remote-app-context';

export async function activateRuntimeFeature(): Promise<void> {
    const moduleRef = await ensureFeatureModuleRef(RuntimeFeatureModule);
    bootstrapCrtModule('<%projectName%>', RuntimeFeatureModule, {
        resolveDependency: (token: unknown) => moduleRef.injector.get(token as ProviderToken<unknown>),
    });
}
