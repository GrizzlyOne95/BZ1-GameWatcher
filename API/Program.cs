using BZAPI.Activity;
using BZAPI.Bot;
using BZAPI.Configuration;
using BZAPI.Maps;
using BZAPI.Steam;
using BZAPI.Storage;
using BZAPI.Websocket;
using Microsoft.AspNetCore.HttpOverrides;

const string CorsPolicyName = "AllowGameWatcherClients";

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BattlezoneOptions>(builder.Configuration.GetSection(BattlezoneOptions.SectionName));
builder.Services.Configure<SteamOptions>(builder.Configuration.GetSection(SteamOptions.SectionName));
builder.Services.Configure<MapMetadataOptions>(builder.Configuration.GetSection(MapMetadataOptions.SectionName));
builder.Services.Configure<LobbyBotOptions>(builder.Configuration.GetSection(LobbyBotOptions.SectionName));
builder.Services.Configure<ChatObserverOptions>(builder.Configuration.GetSection(ChatObserverOptions.SectionName));
builder.Services.Configure<ActivityOptions>(builder.Configuration.GetSection(ActivityOptions.SectionName));

// The production UI is served by this same application, so CORS is normally only needed for local
// development or an intentionally separate client. Configure allowed origins instead of hard-coding them.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        if (allowedOrigins.Length == 0)
        {
            return;
        }

        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Production hosts such as Render terminate TLS in front of the application. Trust the forwarding
// headers supplied by that proxy so generated URLs and request metadata use the public scheme.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ILobbyStore, LobbyStore>();
builder.Services.AddSingleton<IChatStore, ChatStore>();
builder.Services.AddSingleton<IActivityStore, ActivityStore>();
builder.Services.AddSingleton<ISteamAvatarProvider, SteamAvatarProvider>();
builder.Services.AddSingleton<ISteamWorkshopProvider, SteamWorkshopProvider>();
builder.Services.AddSingleton<IMapMetadataProvider, MapMetadataProvider>();
builder.Services.AddSingleton<LobbyBotCoordinator>();
builder.Services.AddSingleton<LobbyConnectionState>();
builder.Services.AddHostedService<BZ98LobbyWatcher>();
builder.Services.AddHostedService<BZ98ChatObserver>();
builder.Services.AddHostedService<ActivitySampler>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
        options.RoutePrefix = string.Empty;
    });
}
else
{
    app.UseExceptionHandler();

    // Dockerfile.render copies the Angular production build into wwwroot. Hosting it here keeps the
    // browser and API same-origin and lets one Render web service wake and deploy as a single unit.
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = context =>
        {
            var path = context.Context.Request.Path;

            // Browsers independently revalidate service-worker scripts, but explicit no-cache
            // avoids a proxy/CDN pinning an old worker or manifest across a deployment.
            if (path.Equals("/sw.js") || path.Equals("/manifest.json"))
            {
                context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
                return;
            }

            if (path.StartsWithSegments("/vehicles") || path.StartsWithSegments("/factions"))
            {
                context.Context.Response.Headers.CacheControl = "public, max-age=86400";
            }
        }
    });
}

app.UseRouting();
app.UseCors(CorsPolicyName);

app.MapControllers();

// Render uses this path for deploy and runtime health checks. Lobby data can legitimately still be
// unchanged for a long time, so the websocket connection state is reported separately from the
// timestamp of the most recent lobby-list mutation.
app.MapGet("/api/health", (
    ILobbyStore store,
    IActivityStore activity,
    LobbyConnectionState lobbyConnection,
    LobbyBotCoordinator bot) =>
{
    var snapshot = store.Current;

    return Results.Ok(new
    {
        status = "ok",
        lobbyCount = snapshot.Lobbies.Count,
        lastUpdatedUtc = snapshot.LastUpdatedUtc,
        lobbyConnection = lobbyConnection.Current,
        activityHistoryStartedUtc = activity.FirstSampleUtc,
        activityLastSampleUtc = activity.LastSampleUtc,
        activityStorage = activity.StorageKind,
        activityDurable = activity.IsDurable,
        lobbyBot = bot.Status
    });
});

if (!app.Environment.IsDevelopment())
{
    // Angular owns all non-file routes. API and static-file endpoints above remain more specific.
    app.MapFallbackToFile("index.html");
}

app.Run();
