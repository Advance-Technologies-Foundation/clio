import { DoBootstrap, Injector, NgModule } from "@angular/core";
import { createCustomElement } from "@angular/elements";
import { BrowserModule } from "@angular/platform-browser";
import { CrtModule } from "@creatio-devkit/common";
import { DemoComponent } from "./view-elements";

@CrtModule({
  viewElements: [DemoComponent],
})
@NgModule({
  declarations: [DemoComponent],
  imports: [BrowserModule],
  providers: [],
})
export class AppModule implements DoBootstrap {
  constructor(private _injector: Injector) {}

  ngDoBootstrap(): void {
    const cmp = createCustomElement(DemoComponent, {
      injector: this._injector,
    });
    customElements.define("<%vendorPrefix%>-demo", cmp);
  }
}
