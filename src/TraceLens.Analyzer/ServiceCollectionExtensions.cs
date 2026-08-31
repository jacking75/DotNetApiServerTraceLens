using Microsoft.Extensions.DependencyInjection;

namespace TraceLens.Analyzer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTraceLensAnalyzer(this IServiceCollection services) => services
        .AddScoped<SolutionLoader>()
        .AddScoped<EntryPointScanner>()
        .AddScoped<DiResolver>()
        .AddScoped<NodeClassifier>()
        .AddScoped<InvocationResolver>()
        .AddScoped<SolutionScanner>()
        .AddScoped<CallGraphWalker>()
        .AddScoped<SnapshotHasher>()
        .AddScoped<StartupFlowScanner>()
        .AddScoped<DataAccessScanner>()
        .AddScoped<CompensationAnalyzer>()
        .AddScoped<DataRuleEngine>()
        .AddScoped<MiddlewareScanner>()
        .AddScoped<ITraceLensAnalyzer, TraceLensAnalyzer>();
}
