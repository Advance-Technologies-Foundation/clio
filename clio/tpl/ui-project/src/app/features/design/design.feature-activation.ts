import { Injector, ProviderToken, Type } from '@angular/core';
import { createCustomElement } from '@angular/elements';
import { bootstrapCrtModule } from '@creatio-devkit/common';

import { ensureFeatureModuleRef } from '../../remote-app-context';
import { DesignFeatureModule } from './design-feature.module';
import { DEMO_PROPERTY_PANEL_SELECTOR } from './design-feature.ids';
import { DemoPropertyPanelComponent } from './property-panels/demo-property-panel.component';

function defineCustomElement(selector: string, component: Type<unknown>, injector: Injector): void {
  if (!customElements.get(selector)) {
    customElements.define(selector, createCustomElement(component, { injector }));
  }
}

export async function activateDesignFeature(): Promise<void> {
  const moduleRef = await ensureFeatureModuleRef(DesignFeatureModule);
  const injector = moduleRef.injector;

  defineCustomElement(DEMO_PROPERTY_PANEL_SELECTOR, DemoPropertyPanelComponent, injector);

  bootstrapCrtModule('<%projectName%>', DesignFeatureModule, {
    resolveDependency: (token: unknown) => moduleRef.injector.get(token as ProviderToken<unknown>),
  });
}
