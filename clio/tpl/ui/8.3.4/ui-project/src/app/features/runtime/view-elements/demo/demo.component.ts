import {
    ChangeDetectionStrategy,
    Component,
    Input,
    ViewEncapsulation,
} from '@angular/core';
import { CrtInput, CrtMobileViewElement, CrtViewElement } from '@creatio-devkit/common';
import {
    DEMO_MOBILE_SELECTOR,
    DEMO_MOBILE_VIEW_ELEMENT_TYPE,
    DEMO_SELECTOR,
    DEMO_VIEW_ELEMENT_TYPE,
} from '../../runtime-feature.ids';

@CrtViewElement({
	selector: DEMO_SELECTOR,
	type: DEMO_VIEW_ELEMENT_TYPE
})
@CrtMobileViewElement({
	selector: DEMO_MOBILE_SELECTOR,
	type: DEMO_MOBILE_VIEW_ELEMENT_TYPE
})
@Component({
    selector: '<%vendorPrefix%>-demo-internal',
    template: `<button type="button" (click)="showAlert()">{{ effectiveLabel }}</button>`,
    changeDetection: ChangeDetectionStrategy.OnPush,
    encapsulation: ViewEncapsulation.ShadowDom,
})
export class DemoComponent {
    @Input()
    @CrtInput()
    public label = '';

    public get effectiveLabel(): string {
        return this.label || 'Click me!';
    }

    public showAlert(): void {
        alert(this.effectiveLabel);
    }
}
