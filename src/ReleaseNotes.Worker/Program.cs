using Microsoft.EntityFrameworkCore;
using ReleaseNotes.Application;
using ReleaseNotes.Infrastructure;
using ReleaseNotes.Infrastructure.Persistence;
using ReleaseNotes.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection(SchedulerOptions.SectionName));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ReleaseNotesSchedulerService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ReleaseNotesDbContext>();
    await db.Database.EnsureCreatedAsync();
    await DatabaseAuthSchemaPatcher.ApplyAsync(db);
}

await app.RunAsync();
