import { Injector, NgModuleRef, Type, createNgModule } from '@angular/core';
import { platformBrowser } from '@angular/platform-browser';
import { firstValueFrom } from 'rxjs';

import { RemoteAppModule } from './remote-app.module';

export interface RemoteAppContext {
  injector: Injector;
  moduleRef: NgModuleRef<RemoteAppModule>;
}

let remoteAppContextPromise: Promise<RemoteAppContext> | undefined;
const featureModuleRefs = new Map<Type<unknown>, NgModuleRef<unknown>>();

async function bootstrapRemoteApp(): Promise<RemoteAppContext> {
  const moduleRef = await platformBrowser().bootstrapModule(RemoteAppModule);
  const injector = moduleRef.injector;

  return {
    injector,
    moduleRef,
  };
}

async function ensureRemoteAppContext(cultureName?: string): Promise<RemoteAppContext> {
  if (!remoteAppContextPromise) {
    remoteAppContextPromise = bootstrapRemoteApp().catch((error: unknown) => {
      remoteAppContextPromise = undefined;
      throw error;
    });
  }
  return remoteAppContextPromise;
}

export async function ensureFeatureModuleRef<T>(moduleType: Type<T>): Promise<NgModuleRef<T>> {
  const existingModuleRef = featureModuleRefs.get(moduleType) as NgModuleRef<T> | undefined;
  if (existingModuleRef) {
    return existingModuleRef;
  }

  const { injector } = await ensureRemoteAppContext();
  const moduleRef = createNgModule(moduleType, injector);
  featureModuleRefs.set(moduleType, moduleRef);
  return moduleRef;
}