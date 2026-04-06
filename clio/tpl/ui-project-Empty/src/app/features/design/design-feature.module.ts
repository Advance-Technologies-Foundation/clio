import { CUSTOM_ELEMENTS_SCHEMA, DoBootstrap, NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CrtModule } from '@creatio-devkit/common';

@CrtModule({
  viewElements: []
})
@NgModule({
  declarations: [],
  imports: [CommonModule],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
})
export class DesignFeatureModule implements DoBootstrap {
  public ngDoBootstrap(): void {
  /*
    Define your property panel custom elements here.
    Example:
    if (!customElements.get(MY_PROPERTY_PANEL_SELECTOR)) {
        const el = createCustomElement(MyPropertyPanelComponent, { injector: this._injector });
        customElements.define(MY_PROPERTY_PANEL_SELECTOR, el);
    }
  */
  }
}
