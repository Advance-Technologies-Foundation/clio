import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CrtModule } from '@creatio-devkit/common';
import { DemoComponent } from './view-elements/demo/demo.component';

@CrtModule({
  viewElements: [DemoComponent]
})
@NgModule({
  declarations: [DemoComponent],
  imports: [CommonModule],
})
export class RuntimeFeatureModule {}
