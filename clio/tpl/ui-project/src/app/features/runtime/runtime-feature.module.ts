import { DoBootstrap, Injector, NgModule } from '@angular/core';
import { createCustomElement } from '@angular/elements';
import { BrowserModule } from '@angular/platform-browser';
import { CrtModule } from '@creatio-devkit/common';
import { DemoComponent } from './view-elements/demo/demo.component';
import { DEMO_SELECTOR } from './runtime-feature.ids';

@CrtModule({
	viewElements: [DemoComponent]
})
@NgModule({
    declarations: [DemoComponent],
    imports: [BrowserModule],
})
export class RuntimeFeatureModule implements DoBootstrap {
    constructor(private readonly _injector: Injector) {}

    public ngDoBootstrap(): void {
        if (!customElements.get(DEMO_SELECTOR)) {
            const elementConstructor = createCustomElement(DemoComponent, { injector: this._injector });
            customElements.define(DEMO_SELECTOR, elementConstructor);
        }
    }
}
