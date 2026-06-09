using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReleaseNotes.Application.Interfaces;
using ReleaseNotes.Infrastructure.Auth;
using ReleaseNotes.Infrastructure.Clients;
using ReleaseNotes.Infrastructure.Options;
using ReleaseNotes.Infrastructure.Persistence;
using ReleaseNotes.Infrastructure.Services;

namespace ReleaseNotes.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GitHubOptions>(configuration.GetSection(GitHubOptions.SectionName));
        services.AddJwtAuthentication(configuration);

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
        services.AddHttpClient(nameof(GitHubApiGitSourceClient), (sp, client) =>
        {
            var apiBaseUrl = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<GitHubOptions>>().Value.ApiBaseUrl;
            client.BaseAddress = new Uri(apiBaseUrl.TrimEnd('/') + "/");
        });

        services.AddScoped<IGitSourceClient, GitHubApiGitSourceClient>();

        services.AddScoped<IRepositoryConnectionReader, RepositoryConnectionReader>();
        services.AddScoped<IRuleEngine, RuleEngine>();
        services.AddScoped<IReleaseNotesRepository, ReleaseNotesRepository>();
        services.AddSingleton<IDistributedLockService, InMemoryLockService>();
        services.AddSingleton<IProgressNotifier, NullProgressNotifier>();
        return services;
    }
}
