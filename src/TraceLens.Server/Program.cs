using TraceLens.Analyzer;
using TraceLens.Server;
using TraceLens.Server.Endpoints;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5080");
builder.Services.AddCors(options => options.AddPolicy("ViteLocal", policy =>
    policy.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<IGraphStore, GraphStore>();
builder.Services.AddSingleton<IGraphFreshnessService, GraphFreshnessService>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<IMcpConfigWriter, McpConfigWriter>();
builder.Services.AddTraceLensAnalyzer();

var app = builder.Build();
app.UseCors("ViteLocal");
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api");
api.MapAnalyzeEndpoints();
api.MapBrowseEndpoints();
api.MapGraphEndpoints();
api.MapSourceEndpoints();
api.MapFreshnessEndpoints();
api.MapAskEndpoints();

if (app.Environment.IsProduction())
{
    app.MapFallbackToFile("index.html");
}

app.Run();

public partial class Program;
