import { Component, Input, OnDestroy, OnInit, ViewEncapsulation } from "@angular/core";
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import {
	CrtInput,
	CrtInterfaceDesignerItem,
	CrtViewElement,
} from "@creatio-devkit/common";
import { SubscriptionLike } from "rxjs";



@CrtViewElement({
	selector: "usr-demo",
	type: "usr.Demo",
	inputs: {
		iframesrc: "",
		title: ""
	},
})
@CrtInterfaceDesignerItem({
  toolbarConfig: {
    caption: "Your component",
    name: "usr-demo",
    icon: require("!!raw-loader?{esModule:false}!./iframe-svgrepo-com.svg"),
  },
})

@Component({
	selector: "usr-demo",
	templateUrl: './demo.component.html',
	styleUrls : ['./demo.component.css'],
	encapsulation: ViewEncapsulation.ShadowDom
  })

export class DemoComponent implements OnInit, OnDestroy {

	DomSanitizer: DomSanitizer
	safeSrc : SafeResourceUrl = "";

	private _subscription?: SubscriptionLike;
	constructor(domSanitizer: DomSanitizer) {
		this.DomSanitizer = domSanitizer;
	}

	//#region Inputs

	//#region Src
	private _iframesrc : string = "";
	public get iframesrc() : string {
		return this._iframesrc;
	}

	@Input() 
	@CrtInput()
	public set iframesrc(v : string) {
		this._iframesrc = v;
		this.safeSrc = this.DomSanitizer.bypassSecurityTrustResourceUrl(v);
	}
	//#endregion

	//#region Title
	private _title : string = "YouTube video player";
	public get title() : string {
		return this._title;
	}

	@Input() 
	@CrtInput()
	public set title(v : string) {
		this._title = v;
	}
	//#endregion

	//#endregion


	ngOnInit(): void {
		
	}
 	 public ngOnDestroy() {
		this._subscription?.unsubscribe();
	}
}