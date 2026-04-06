import { BrowserModule } from '@angular/platform-browser';
import { DoBootstrap, NgModule } from '@angular/core';

@NgModule({
  imports: [BrowserModule],
})
export class RemoteAppModule implements DoBootstrap {
  public ngDoBootstrap(): void {
  }
}