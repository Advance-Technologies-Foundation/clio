import {DoBootstrap, Injector, NgModule, ProviderToken} from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import {bootstrapCrtModule, CrtModule} from '@creatio-devkit/common';

@CrtModule({
  /* Specify that InputComponent is a view element. */
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
    /* Bootstrap CrtModule definitions. */
    bootstrapCrtModule('<%projectName%>', AppModule, {
      resolveDependency: (token) => this._injector.get(<ProviderToken<unknown>>token)
    });
  }
}