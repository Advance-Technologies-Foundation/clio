import { ProviderToken } from '@angular/core';
import { bootstrapCrtModule } from '@creatio-devkit/common';
import { RuntimeFeatureModule } from './runtime-feature.module';
import { ensureFeatureModuleRef } from '../../remote-app-context';

export async function activateRuntimeFeature(): Promise<void> {
  const moduleRef = await ensureFeatureModuleRef(RuntimeFeatureModule);
  /*
    Define your custom elements here.
    Example:
    if (!customElements.get(MY_COMPONENT_SELECTOR)) {
        const el = createCustomElement(MyComponent, { injector: this._injector });
        customElements.define(MY_COMPONENT_SELECTOR, el);
    }
  */
  bootstrapCrtModule('<%projectName%>', RuntimeFeatureModule, {
    resolveDependency: (token: unknown) => moduleRef.injector.get(token as ProviderToken<unknown>),
  });
}
