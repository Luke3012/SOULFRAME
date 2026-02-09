import { AvaturnSDK } from "https://cdn.jsdelivr.net/npm/@avaturn/sdk/dist/index.js";

/** Qui usiamo URL nel formato https://${subdomain}.avaturn.dev o ricevuto dalle API. */
function setupIframe(url, version, platform) {

    const container = document.getElementById("avaturn-sdk-container");

    // Inizializziamo SDK e callback
    const sdk = new AvaturnSDK();

	window.avaturnSDKEnvironment = JSON.stringify({ engine: 'Unity', version, platform });
	sdk.init(container, { url })
	.then(() => sdk.on('export',
	  (data) => {
        
		const params = {};
		['avatarId', 'avatarSupportsFaceAnimations', 'bodyId', 'gender', 'sessionId', 'url', 'urlType'].forEach( (p) => {
		  params[p]= data[p] || '';
		})
		
        if(params.urlType == 'dataURL') {
            params.url = window.URL.createObjectURL(_dataURItoBlob(data.url));
        }

        gameInstance.SendMessage(
            "AvatarReceiver",
            "ReceiveAvatarLink",
            JSON.stringify(params)
        );
	  })
	);

}

function displayIframe() {
    console.log("Display");
    document.getElementById("avaturn-sdk-container").style.display = "block";
}

function hideIframe() {
    console.log("Hide");
    document.getElementById("avaturn-sdk-container").style.display = "none";
}


// Qui teniamo la sezione privata
function _dataURItoBlob(dataURI) {
    let mime = dataURI.split(',')[0].split(':')[1].split(';')[0];
    let binary = atob(dataURI.split(',')[1]);
    let array = [];
    for (let i = 0; i < binary.length; i++) {
        array.push(binary.charCodeAt(i));
    }
    return new Blob([new Uint8Array(array)], {
        type: mime
    });
}
window.setupIframe = setupIframe;
window.hideIframe = hideIframe;
window.displayIframe = displayIframe;
