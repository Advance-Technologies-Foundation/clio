import { enableProdMode } from '@angular/core';
import { platformBrowserDynamic } from '@angular/platform-browser-dynamic';
import { bootstrapCrtModule } from '@creatio-devkit/common';

import { AppModule } from './app/app.module';
import { environment } from './environments/environment';

if (environment.production) {
  enableProdMode();
}

bootstrapCrtModule(AppModule);

platformBrowserDynamic().bootstrapModule(AppModule)
    .catch(err => console.error(err));
