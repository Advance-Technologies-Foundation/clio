import type {
    RemoteDesignerDefinitionsLoadContext,
    RemoteFeatureDesignerDefinitions,
} from '@creatio-devkit/common';
import { DEMO_PROPERTY_PANEL_TYPE } from '../design/design-feature.ids';
import {
    DEMO_MOBILE_VIEW_ELEMENT_TYPE,
    DEMO_VIEW_ELEMENT_TYPE,
} from './runtime-feature.ids';

const DEMO_ICON = `
  <svg xmlns="http://www.w3.org/2000/svg" width="72" height="64" viewBox="0 0 72 64">
    <rect width="72" height="64" rx="12" fill="#f4f7fb"></rect>
    <rect x="14" y="18" width="44" height="28" rx="8" fill="#ff6534"></rect>
    <circle cx="36" cy="32" r="7" fill="#ffffff"></circle>
  </svg>
`;

export async function loadRuntimeDesignerDefinitions(
    context: RemoteDesignerDefinitionsLoadContext,
): Promise<RemoteFeatureDesignerDefinitions> {
    return {
        viewElements: [
            {
                type: DEMO_VIEW_ELEMENT_TYPE,
                toolbarConfig: {
                    caption: 'Demo component',
                    icon: DEMO_ICON,
                },
                defaultPropertyValues: {
                    label: 'Click me!',
                },
                propertiesPanel: DEMO_PROPERTY_PANEL_TYPE,
            },
        ],
        mobileViewElements: [
            {
                type: DEMO_MOBILE_VIEW_ELEMENT_TYPE,
                toolbarConfig: {
                    caption: 'Mobile demo component',
                    icon: DEMO_ICON,
                },
                defaultPropertyValues: {
                    label: 'Click me!',
                },
                propertiesPanel: DEMO_PROPERTY_PANEL_TYPE,
            },
        ],
    };
}
