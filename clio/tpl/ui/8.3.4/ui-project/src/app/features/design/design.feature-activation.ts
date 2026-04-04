import { ProviderToken } from '@angular/core';
import { platformBrowser } from '@angular/platform-browser';
import { bootstrapCrtModule } from '@creatio-devkit/common';
import { DesignFeatureModule } from './design-feature.module';

export async function activateDesignFeature(): Promise<void> {
    const moduleRef = await platformBrowser().bootstrapModule(DesignFeatureModule);
    bootstrapCrtModule('<%projectName%>', DesignFeatureModule, {
        resolveDependency: (token: unknown) => moduleRef.injector.get(token as ProviderToken<unknown>),
    });
}
