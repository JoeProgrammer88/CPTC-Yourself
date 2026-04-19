using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using CptcYourself.Client;
using CptcYourself.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Stateless HttpClient for Google AI calls (no base address needed — full URLs are used)
builder.Services.AddScoped(_ => new HttpClient());

// App services
builder.Services.AddSingleton<AppStateService>();
builder.Services.AddScoped<CryptoStorageService>();
builder.Services.AddScoped<CameraService>();
builder.Services.AddScoped<GoogleAiService>();

await builder.Build().RunAsync();
