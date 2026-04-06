import { CUSTOM_ELEMENTS_SCHEMA, DoBootstrap, Injector, NgModule } from '@angular/core';
import { createCustomElement } from '@angular/elements';
import { CommonModule } from '@angular/common';
import { CrtModule } from '@creatio-devkit/common';
import { DemoPropertyPanelComponent } from './property-panels/demo-property-panel.component';
import { DEMO_PROPERTY_PANEL_SELECTOR } from './design-feature.ids';

@CrtModule({
  viewElements: [DemoPropertyPanelComponent]
})
@NgModule({
  declarations: [DemoPropertyPanelComponent],
  imports: [CommonModule],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
})
export class DesignFeatureModule {}
