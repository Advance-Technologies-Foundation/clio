import { BrowserModule } from '@angular/platform-browser';
import { DoBootstrap, NgModule } from '@angular/core';

@NgModule({
  imports: [BrowserModule],
})
export class RemoteAppModule implements DoBootstrap {
  /**
   * This method must exist to bootstrap the module.
  */
  public ngDoBootstrap(): void {
  }
}