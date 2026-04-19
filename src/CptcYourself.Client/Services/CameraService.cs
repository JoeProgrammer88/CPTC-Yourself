using Microsoft.JSInterop;

namespace CptcYourself.Client.Services;

public class CameraService(IJSRuntime js)
{
    public async Task StartAsync(string videoElementId) =>
        await js.InvokeVoidAsync("cptcInterop.startCamera", videoElementId);

    public async Task StopAsync(string videoElementId) =>
        await js.InvokeVoidAsync("cptcInterop.stopCamera", videoElementId);

    /// <summary>Returns a raw base64 JPEG string (no data-URI prefix).</summary>
    public async Task<string> CapturePhotoAsync(string videoElementId) =>
        await js.InvokeAsync<string>("cptcInterop.capturePhoto", videoElementId);
}
