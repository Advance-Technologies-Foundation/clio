for(var i = 0; i < response.headers.valuesOf("Set-Cookie").length; i++) {
    let cookie = response.headers.valuesOf("Set-Cookie")[i];
    let cName = cookie.substring(0,cookie.indexOf("="));
    if(cName==="BPMCSRF"){
        let cValue = cookie.substring(cookie.indexOf("=") + 1, cookie.indexOf(";"));
        client.global.set("BPMCSRF", cValue);
    }
}