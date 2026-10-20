using MailGester;
using MailGester.Api;
using MailGester.Auth;
using MailGester.Data;
using MailGester.Digest;
using MailGester.Runtime;
using MailGester.Sources;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

var paths = new DataPaths(builder.Configuration);
builder.Services.AddSingleton(paths);

// Keys live in the data volume, otherwise stored secrets could not be decrypted after a container rebuild.
builder.Services.AddDataProtection()
    .SetApplicationName("MailGester")
    .PersistKeysToFileSystem(new DirectoryInfo(paths.Keys));

builder.Services.AddAuthentication(AuthEndpoints.Scheme).AddCookie(AuthEndpoints.Scheme, options =>
{
    options.Cookie.Name = "mailgester.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();

builder.Services.AddHttpClient();
builder.Services.AddHttpClient(OllamaAdmin.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<KvStore>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<AccountRepository>();
builder.Services.AddSingleton<DigestRepository>();
builder.Services.AddSingleton<AdminAuth>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<MsalTokenProvider>();
builder.Services.AddSingleton<OutlookSignInService>();
builder.Services.AddSingleton<MailSourceFactory>();
builder.Services.AddSingleton<OllamaAdmin>();
builder.Services.AddSingleton<OllamaDigestGenerator>();
builder.Services.AddSingleton<DiscordNotifier>();
builder.Services.AddSingleton<RunCoordinator>();
builder.Services.AddSingleton<RunStatus>();
builder.Services.AddSingleton<DigestRunner>();
builder.Services.AddHostedService<Worker>();

var app = builder.Build();

await app.Services.GetRequiredService<Db>().InitAsync();
await app.Services.GetRequiredService<SettingsService>().LoadAsync();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// SameSite=Strict already blocks cross site requests, the header is a second layer for anything that mutates.
var api = app.MapGroup("/api").AddEndpointFilter(async (context, next) =>
{
    var request = context.HttpContext.Request;
    var mutates = !HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method);

    return mutates && request.Headers["X-Requested-With"] != "MailGester"
        ? Results.StatusCode(StatusCodes.Status403Forbidden)
        : await next(context);
});

api.MapAuth();

var secured = api.MapGroup("").RequireAuthorization();
secured.MapSettings();
secured.MapAccounts();
secured.MapRun();

app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
