import {Component, Input} from "@angular/core";
import {
  CrtInterfaceDesignerItem,
  CrtViewElement,
} from "@creatio-devkit/common";

@CrtViewElement({
  selector: "<%vendorPrefix%>-demo",
  type: "<%vendorPrefix%>.Demo",
})
@CrtInterfaceDesignerItem({
  toolbarConfig: {
    caption: "Your component",
    name: "<%vendorPrefix%>-demo",
    icon: require("!!raw-loader?{esModule:false}!./demo-icon.svg"),
  },
})
@Component({
  selector: "<%vendorPrefix%>-demo",
  template: `<button (click)="showAlert()">Click me!</button>`,
})
export class DemoComponent {
  public showAlert() {
    alert("Congrats, welcome to Freedom!");
  }
}