import { ProviderToken } from '@angular/core';
import { bootstrapCrtModule } from '@creatio-devkit/common';
import { DesignFeatureModule } from './design-feature.module';
import { ensureFeatureModuleRef } from '../../remote-app-context';

export async function activateDesignFeature(): Promise<void> {
  const moduleRef = await ensureFeatureModuleRef(DesignFeatureModule);
  bootstrapCrtModule('<%projectName%>', DesignFeatureModule, {
    resolveDependency: (token: unknown) => moduleRef.injector.get(token as ProviderToken<unknown>),
  });
}
