import { Injector, NgModuleRef, Type, createNgModule } from '@angular/core';
import { platformBrowser } from '@angular/platform-browser';

import { RemoteAppModule } from './remote-app.module';

export interface RemoteAppContext {
  injector: Injector;
  moduleRef: NgModuleRef<RemoteAppModule>;
}

let remoteAppContextPromise: Promise<RemoteAppContext> | undefined;
const featureModuleRefs = new Map<Type<unknown>, NgModuleRef<unknown>>();

/**
 * Bootstraps the root remote application module and exposes the injector-based runtime context.
 *
 * @returns A remote application context that contains the root injector and module reference.
 */
async function bootstrapRemoteApp(): Promise<RemoteAppContext> {
  const moduleRef = await platformBrowser().bootstrapModule(RemoteAppModule);
  const injector = moduleRef.injector;

  return {
    injector,
    moduleRef,
  };
}

/**
 * Returns a shared remote application context, creating it once and reusing it across callers.
 *
 * If bootstrap fails, the cached promise is cleared so the next call can retry initialization.
 *
 * @param cultureName Optional culture name reserved for context initialization.
 * @returns The cached remote application context.
 */
async function ensureRemoteAppContext(cultureName?: string): Promise<RemoteAppContext> {
  if (!remoteAppContextPromise) {
    remoteAppContextPromise = bootstrapRemoteApp().catch((error: unknown) => {
      remoteAppContextPromise = undefined;
      throw error;
    });
  }
  return remoteAppContextPromise;
}

/**
 * Returns a cached feature module reference or creates one from the remote application injector.
 *
 * @param moduleType The Angular feature module type to resolve.
 * @returns The existing or newly created module reference for the requested feature module.
 */
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
