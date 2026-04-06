import { ProviderToken } from '@angular/core';
import { bootstrapCrtModule } from '@creatio-devkit/common';
import { DesignFeatureModule } from './design-feature.module';
import { ensureFeatureModuleRef } from '../../remote-app-context';

export async function activateDesignFeature(): Promise<void> {
  const moduleRef = await ensureFeatureModuleRef(DesignFeatureModule);
  /*
    Define your property panel custom elements here.
    Example:
    if (!customElements.get(MY_PROPERTY_PANEL_SELECTOR)) {
        const el = createCustomElement(MyPropertyPanelComponent, { injector: this._injector });
        customElements.define(MY_PROPERTY_PANEL_SELECTOR, el);
    }
  */
  bootstrapCrtModule('<%projectName%>', DesignFeatureModule, {
    resolveDependency: (token: unknown) => moduleRef.injector.get(token as ProviderToken<unknown>),
  });
}
