import { ProviderToken } from '@angular/core';
import { platformBrowser } from '@angular/platform-browser';
import { bootstrapCrtModule } from '@creatio-devkit/common';
import { RuntimeFeatureModule } from './runtime-feature.module';

export async function activateRuntimeFeature(): Promise<void> {
    const moduleRef = await platformBrowser().bootstrapModule(RuntimeFeatureModule);
    bootstrapCrtModule('<%projectName%>', RuntimeFeatureModule, {
        resolveDependency: (token: unknown) => moduleRef.injector.get(token as ProviderToken<unknown>),
    });
}
