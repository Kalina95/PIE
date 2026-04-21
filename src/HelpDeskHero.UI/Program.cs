using HelpDeskHero.UI;
using HelpDeskHero.UI.Services;
using HelpDeskHero.UI.Services.Api;
using HelpDeskHero.UI.Services.Auth;
using HelpDeskHero.UI.Services.Realtime;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var apiBaseUrl = builder.Configuration["Api:BaseUrl"]
    ?? throw new InvalidOperationException("Missing Api:BaseUrl.");

builder.Services.AddAuthorizationCore(options =>
{
    options.AddPolicy("CanManageTickets", policy => policy.RequireRole("Admin", "Agent"));
    options.AddPolicy("CanViewAudit", policy => policy.RequireRole("Admin"));
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
    options.AddPolicy("AgentOrAdmin", policy => policy.RequireRole("Agent", "Admin"));
});

builder.Services.AddScoped<TokenStore>();
builder.Services.AddScoped<JwtAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthenticationStateProvider>());
builder.Services.AddScoped<AuthHttpMessageHandler>();

builder.Services.AddHttpClient("AnonymousApi", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
});

builder.Services.AddHttpClient("Api", client =>
{
    client.BaseAddress = new Uri(apiBaseUrl);
})
.AddHttpMessageHandler<AuthHttpMessageHandler>();

builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api"));
builder.Services.AddScoped<AuthApiClient>();
builder.Services.AddScoped<ITicketApiClient, TicketApiClient>();
builder.Services.AddScoped<ITicketsRealtimeClient, TicketsRealtimeClient>();
builder.Services.AddScoped<DashboardApiClient>();
builder.Services.AddScoped<TicketCommentApiClient>();
builder.Services.AddScoped<TicketAttachmentApiClient>();
builder.Services.AddScoped<NotificationApiClient>();
builder.Services.AddScoped<NotificationUnreadState>();

await builder.Build().RunAsync();
