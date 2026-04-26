using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Infrastructure.Clients;
using ReleaseNotes.Infrastructure.Options;
using ReleaseNotes.Infrastructure.Persistence;
using ReleaseNotes.Infrastructure.Services;

namespace ReleaseNotes.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GitHubOptions>(configuration.GetSection("GitHub"));

        services.AddDbContext<ReleaseNotesDbContext>(opts =>
        {
            var sqlConnection = configuration.GetConnectionString("Sql");
            if (!string.IsNullOrWhiteSpace(sqlConnection))
            {
                opts.UseSqlServer(sqlConnection);
            }
            else
            {
                opts.UseSqlite("Data Source=release-notes.db");
            }
        });

        services.AddMemoryCache();
        services.AddStackExchangeRedisCache(opts =>
        {
            opts.Configuration = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        });

        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(retryAttempt));

        services.AddHttpClient<IGitSourceClient, GitHubApiClient>()
            .AddPolicyHandler(retryPolicy)
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddScoped<IRuleEngine, RuleEngine>();
        services.AddScoped<IReleaseNotesRepository, ReleaseNotesRepository>();
        services.AddScoped<IDistributedLockService, RedisLockService>();
        services.AddSingleton<IProgressNotifier, NullProgressNotifier>();
        return services;
    }
}
