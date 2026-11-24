using OAuthSamples;

if (args.Contains("selftest")) return await SelfTest.RunAsync();

var builder = WebApplication.CreateBuilder(args);
const string resource = "https://localhost:5001/api";
const string issuer = "https://as.example.test";
builder.AddApiAuth(issuer, resource, Metadata.WellKnown(new Uri(resource), "oauth-protected-resource").ToString());
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapProtectedResourceMetadata(resource, issuer);
app.MapGet("/api/orders", () => Results.Ok(new[] { "o1" })).RequireAuthorization("orders:read");
app.Run();
return 0;
