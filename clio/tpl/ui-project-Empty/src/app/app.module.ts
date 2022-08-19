import { DoBootstrap, Injector, NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { CrtModule } from '@creatio-devkit/common';

@CrtModule({
  viewElements: [],
})
@NgModule({
  declarations: [],
  imports: [BrowserModule],
  providers: [],
})
export class AppModule implements DoBootstrap {
  constructor(private _injector: Injector) {}

  ngDoBootstrap(): void {
  }
}
