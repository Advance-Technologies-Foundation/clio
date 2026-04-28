import { CUSTOM_ELEMENTS_SCHEMA, NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CrtModule } from '@creatio-devkit/common';
import { DemoPropertyPanelComponent } from './property-panels/demo-property-panel.component';

@CrtModule({
  viewElements: [DemoPropertyPanelComponent]
})
@NgModule({
  declarations: [DemoPropertyPanelComponent],
  imports: [CommonModule],
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
})
export class DesignFeatureModule {}
