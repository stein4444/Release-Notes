using Microsoft.EntityFrameworkCore;

namespace ReleaseNotes.Infrastructure.Persistence;

public static class DbUpdateExceptionExtensions
{
    public static bool IsUniqueConstraintViolation(this DbUpdateException ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            var m = e.Message;
            if (m.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                || m.Contains("SQLite Error 19:", StringComparison.OrdinalIgnoreCase)
                || m.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                || m.Contains("Cannot insert duplicate key", StringComparison.OrdinalIgnoreCase)
                || m.Contains("23505", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
