import { Injector, ProviderToken, Type } from '@angular/core';
import { createCustomElement } from '@angular/elements';
import { bootstrapCrtModule } from '@creatio-devkit/common';

import { ensureFeatureModuleRef } from '../../remote-app-context';
import { RuntimeFeatureModule } from './runtime-feature.module';
import { DEMO_SELECTOR } from './runtime-feature.ids';
import { DemoComponent } from './view-elements/demo/demo.component';

function defineCustomElement(selector: string, component: Type<unknown>, injector: Injector): void {
  if (!customElements.get(selector)) {
    customElements.define(selector, createCustomElement(component, { injector }));
  }
}

/**
 * Initializes the runtime feature module, registers its custom elements, and boots the Creatio runtime integration.
 */
export async function activateRuntimeFeature(): Promise<void> {
  const moduleRef = await ensureFeatureModuleRef(RuntimeFeatureModule);
  const injector = moduleRef.injector;

  defineCustomElement(DEMO_SELECTOR, DemoComponent, injector);

  bootstrapCrtModule('<%projectName%>', RuntimeFeatureModule, {
    resolveDependency: (token: unknown) => moduleRef.injector.get(token as ProviderToken<unknown>),
  });
}
