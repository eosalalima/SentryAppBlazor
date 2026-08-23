using Microsoft.EntityFrameworkCore;
using SentryAppBlazor.Data;

namespace SentryAppBlazor.Turnstile;
public sealed class PersonnelLookupService(IDbContextFactory<StaffDbContext> staffFactory, IDbContextFactory<StudentDbContext> studentFactory, ILogger<PersonnelLookupService> logger)
{
    public async Task<string?> FindMobileAsync(string? accessNumber, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(accessNumber)) return null;
        try
        {
            await using var staff = await staffFactory.CreateDbContextAsync(token); await using var student = await studentFactory.CreateDbContextAsync(token);
            var staffTask = staff.People.Where(x => x.Field15 == accessNumber).Select(x => x.Field13).FirstOrDefaultAsync(token);
            var studentTask = student.People.Where(x => x.Field15 == accessNumber).Select(x => x.Field13).FirstOrDefaultAsync(token);
            await Task.WhenAll(staffTask, studentTask);
            return Normalize(await staffTask) ?? Normalize(await studentTask);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "Mobile lookup failed for access number {AccessNumber}", accessNumber); return null; }
    }
    private static string? Normalize(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; var v = value.Trim(); return v.Length is >= 7 and <= 20 && v.All(c => char.IsDigit(c) || c == '+') ? v : null; }
}
