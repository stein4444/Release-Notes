using Microsoft.Extensions.DependencyInjection;
using ReleaseNotes.Application.Interfaces;

namespace ReleaseNotes.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IGenerateReleaseNotesUseCase, GenerateReleaseNotesUseCase>();
        return services;
    }
}
