// src/ProjectName.ServiceDefaults/OllamaExtensions.cs
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration; // 👈 CRITICAL: Resolves CS1061
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OllamaSharp;

namespace ProjectName.ServiceDefaults;

public static class OllamaExtensions
{
    public static class Keys
    {
        public const string Orchestrator = "model-orchestrator";
    }

    public static TBuilder AddOllamaClients<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        // builder.Configuration is an IConfigurationManager. 
        // We use it as IConfiguration to access extension methods.
        IConfiguration config = builder.Configuration;

        builder.Services.AddKeyedSingleton<IChatClient>(Keys.Orchestrator, (sp, _) =>
        {
            var cs = config.GetConnectionString("ollama-server")
                  ?? "http://localhost:11434";

            var model = config["Ollama:Models:Orchestrator"] ?? "qwen3:8b";

            var http = new HttpClient { BaseAddress = new Uri(cs), Timeout = Timeout.InfiniteTimeSpan };
            return new OllamaApiClient(http) { SelectedModel = model };
        });

        return builder;
    }
}