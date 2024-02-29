import {DoBootstrap, Injector, NgModule, ProviderToken} from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import {bootstrapCrtModule, CrtModule} from '@creatio-devkit/common';
import { DemoComponent } from './view-elements/demo/demo.component';
import {createCustomElement} from "@angular/elements";

@CrtModule({
  /* Specify that InputComponent is a view element. */
  viewElements: [DemoComponent],
})
@NgModule({
  declarations: [
    DemoComponent
  ],
  imports: [BrowserModule],
  providers: [],
})
export class AppModule implements DoBootstrap {
  constructor(private _injector: Injector) {}

  ngDoBootstrap(): void {
    /* Register InputComponent as an Angular Element. */
    const cmp = createCustomElement(DemoComponent, {
      injector: this._injector,
    });
    customElements.define("usr-demo", cmp);
    /* Bootstrap CrtModule definitions. */
    bootstrapCrtModule('iframeintegration', AppModule, {
      resolveDependency: (token) => this._injector.get(<ProviderToken<unknown>>token)
    });
  }
}