using Microsoft.AspNetCore.ResponseCompression;
using TraceLens.Analyzer;
using TraceLens.Server;
using TraceLens.Server.Endpoints;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:25011");
builder.Services.AddCors(options => options.AddPolicy("ViteLocal", policy =>
    policy.WithOrigins("http://localhost:25012").AllowAnyHeader().AllowAnyMethod()));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// members가 들어가면서 /api/graph 응답이 커졌다. 로컬이라도 수 MB JSON은 파싱 전 전송에서 체감된다.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
});
builder.Services.AddSingleton<IGraphStore, GraphStore>();
builder.Services.AddSingleton<IGraphFreshnessService, GraphFreshnessService>();
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<IMcpConfigWriter, McpConfigWriter>();
builder.Services.AddTraceLensAnalyzer();

var app = builder.Build();
app.UseResponseCompression();
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
