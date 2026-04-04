import { CUSTOM_ELEMENTS_SCHEMA, DoBootstrap, Injector, NgModule } from '@angular/core';
import { createCustomElement } from '@angular/elements';
import { BrowserModule } from '@angular/platform-browser';
import { CrtModule } from '@creatio-devkit/common';
import { DemoPropertyPanelComponent } from './property-panels/demo-property-panel.component';
import { DEMO_PROPERTY_PANEL_SELECTOR } from './design-feature.ids';

@CrtModule({
	viewElements: [DemoPropertyPanelComponent]
})
@NgModule({
    declarations: [DemoPropertyPanelComponent],
    imports: [BrowserModule],
    schemas: [CUSTOM_ELEMENTS_SCHEMA],
})
export class DesignFeatureModule implements DoBootstrap {
    constructor(private readonly _injector: Injector) {}

    public ngDoBootstrap(): void {
        if (!customElements.get(DEMO_PROPERTY_PANEL_SELECTOR)) {
            const elementConstructor = createCustomElement(DemoPropertyPanelComponent, { injector: this._injector });
            customElements.define(DEMO_PROPERTY_PANEL_SELECTOR, elementConstructor);
        }
    }
}
