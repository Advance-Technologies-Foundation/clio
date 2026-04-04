import { CUSTOM_ELEMENTS_SCHEMA, DoBootstrap, NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { CrtModule } from '@creatio-devkit/common';

@CrtModule({
	viewElements: []
})
@NgModule({
    declarations: [],
    imports: [BrowserModule],
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
