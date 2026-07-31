using Engine.Chess.Board;
using Engine.UI;
using Engine.UI.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// One session per tab: it owns the game, the bot and the analysis engine.
builder.Services.AddSingleton<GameSession>();
builder.Services.AddScoped<SoundPlayer>();

// Building the attack tables costs a few milliseconds. Doing it during startup
// keeps it off the first move the player makes.
Attacks.Initialize();

await builder.Build().RunAsync();
