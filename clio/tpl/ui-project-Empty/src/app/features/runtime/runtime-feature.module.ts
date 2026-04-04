import { DoBootstrap, NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { CrtModule } from '@creatio-devkit/common';

@CrtModule({
	viewElements: []
})
@NgModule({
    declarations: [],
    imports: [BrowserModule],
})
export class RuntimeFeatureModule implements DoBootstrap {
    public ngDoBootstrap(): void {
        /*
            Define your custom elements here.
            Example:
            if (!customElements.get(MY_COMPONENT_SELECTOR)) {
                const el = createCustomElement(MyComponent, { injector: this._injector });
                customElements.define(MY_COMPONENT_SELECTOR, el);
            }
        */
    }
}
