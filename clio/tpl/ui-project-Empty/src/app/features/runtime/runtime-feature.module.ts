import { DoBootstrap, NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CrtModule } from '@creatio-devkit/common';

@CrtModule({
	viewElements: []
})
@NgModule({
    declarations: [],
    imports: [CommonModule],
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
