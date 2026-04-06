import {
  ChangeDetectionStrategy,
  Component,
  Input,
  signal,
  ViewEncapsulation,
} from '@angular/core';
import { CrtInput, CrtViewElement } from '@creatio-devkit/common';
import { ViewNodeEditor, ViewNodePropertyValueType } from '@creatio/interface-designer';
import { DEMO_PROPERTY_PANEL_SELECTOR, DEMO_PROPERTY_PANEL_TYPE } from '../design-feature.ids';

@CrtViewElement({
  selector: DEMO_PROPERTY_PANEL_SELECTOR,
  type: DEMO_PROPERTY_PANEL_TYPE,
})
@Component({
  selector: '<%vendorPrefix%>-demo-property-panel-internal',
  templateUrl: './demo-property-panel.component.html',
  styleUrls: ['./demo-property-panel.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
  standalone: false,
})
export class DemoPropertyPanelComponent {
  private _viewNodeEditor!: ViewNodeEditor;

  protected readonly label = signal('');
  protected readonly isPanelReady = signal(false);

  @Input()
  @CrtInput()
  public set viewNodeEditor(value: ViewNodeEditor) {
    this._viewNodeEditor = value;
    void this._loadState(value);
  }

  private async _loadState(editor: ViewNodeEditor): Promise<void> {
    this.isPanelReady.set(false);
    const propertyValue = await editor.getPropertyValue('label');
    this.label.set(
      propertyValue?.type === ViewNodePropertyValueType.Constant &&
        typeof propertyValue.value === 'string'
      ? propertyValue.value
      : '',
    );
    this.isPanelReady.set(true);
  }

  protected async handleLabelInput(event: Event): Promise<void> {
    const value = (event.target as HTMLInputElement).value;
    this.label.set(value);
    await this._viewNodeEditor.setPropertyValue('label', { constant: value });
  }
}
