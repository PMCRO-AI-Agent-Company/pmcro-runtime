using Polly;
using ProjectName.OrchestratorService;
using ProjectName.OrchestratorService.Configuration;
using ProjectName.ServiceDefaults;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --- Aspire Defaults ---
builder.AddServiceDefaults();

// --- Ollama IChatClient ("model-orchestrator") for trail replay ---
// Reads connection string "ollama-server" + "Ollama:Models:Orchestrator".
builder.AddOllamaClients();

// --- Controllers & OpenAPI ---
// Only discover controllers in THIS assembly, ignore referenced projects
builder.Services.AddControllers()
    .AddApplicationPart(typeof(Program).Assembly); 
builder.Services.AddEndpointsApiExplorer();

// Generates the OpenAPI spec that Scalar will consume
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "PMCR-O Colony API";
        document.Info.Version = "v5.1";
        document.Info.Description = "Thin HTTP facade over OrchestratorService (gRPC). Provides synchronous and asynchronous cognitive loops.";
        return Task.CompletedTask;
    });
});

// --- CORS ---
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// --- gRPC Client (OrchestratorService) ---
var grpcClient = builder.Services
    .AddGrpcClient<Orchestrator.OrchestratorClient>(o =>
        o.Address = new Uri("https://orchestratorservice"));

// PMCRO cycles are long-running (Ollama inference + HIL wait).
// Remove the global 30-second standard resilience handler, and apply a 10-minute timeout.
grpcClient.RemoveAllResilienceHandlers();
grpcClient.AddResilienceHandler("pmcro-long-running", pipeline =>
{
    pipeline.AddTimeout(TimeSpan.FromMinutes(10));
});

grpcClient.AddServiceDiscovery();

// Register OrchestratorConfig so TrailReader can read FileSystemRoot
// (same config key used by FileTrailWriter in OrchestratorService for trail identity)
builder.Services.Configure<OrchestratorConfig>(builder.Configuration.GetSection(OrchestratorConfig.SectionName));
builder.Services.AddScoped<ProjectName.OrchestratorApi.Services.TrailReader>();

// Round Table controller writes/reads the session trail directly against disk
// (same FileSystemRoot as FileTrailWriter/TrailReader above), not via the gRPC
// client — see Controllers/RoundTableController.cs SCOPE NOTE.
builder.Services.AddScoped<ProjectName.OrchestratorService.Services.ITrailWriter, ProjectName.OrchestratorService.Services.FileTrailWriter>();

// Skills catalog -- HTTP facade over the real <FileSystemRoot>/skills tree.
// See Services/SkillCatalogService.cs and Controllers/SkillsController.cs.
builder.Services.AddScoped<ProjectName.OrchestratorApi.Services.SkillCatalogService>();

// Allow internal self-signed certs
grpcClient.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    ServerCertificateCustomValidationCallback =
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});

var app = builder.Build();

// --- Middleware Pipeline ---
app.UseCors();
app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    // Generate the /openapi/v1.json spec
    app.MapOpenApi();

    // Serve Scalar UI at the root path (/)
    app.MapScalarApiReference("/", options =>
    {
        options.WithTitle("PMCR-O API Reference")
               .WithTheme(ScalarTheme.Moon)
               .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
    });
}

// Map the [ApiController] routes
app.MapControllers();

// Health check endpoint
app.MapGet("/healthz", () => "ProjectName.OrchestratorApi -- HTTP facade for OrchestratorService");

app.Run();