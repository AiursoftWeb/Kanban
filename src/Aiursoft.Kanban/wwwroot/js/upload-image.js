
async function uploadImageToServer(blob, kanbanImageUploadUrl) {
    var formData = new FormData();
    var ext = blob.type === 'image/png' ? 'png' :
        blob.type === 'image/gif' ? 'gif' :
            blob.type === 'image/webp' ? 'webp' : 'jpg';
    formData.append('file', blob, 'upload-' + Date.now() + '.' + ext);

    var uploadResp = await fetch(kanbanImageUploadUrl, {
        method: 'POST',
        body: formData
    });
    if (!uploadResp.ok) {
        var errText = await uploadResp.text();
        throw new Error(errText || 'Upload failed. Status: ' + uploadResp.status);
    }
    return await uploadResp.json();
}
