using FluentAssertions;
using MediCare.Data.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace MediCare.Tests.Integration;

[Trait("Category", "Integration")]
[Collection("SqlServerIntegration")]
public class AppointmentCheckInMigrationCompatibilityIntegrationTests
{
    private readonly string _baseConnectionString;

    public AppointmentCheckInMigrationCompatibilityIntegrationTests(SqlServerDatabaseFixture fixture)
    {
        _baseConnectionString = fixture.ConnectionString;
    }

    private (ApplicationDbContext Context, string DbName) CreateIsolatedContext()
    {
        var builder = new SqlConnectionStringBuilder(_baseConnectionString)
        {
            InitialCatalog = $"MediCare_MigTest_{Guid.NewGuid():N}",
            ConnectTimeout = 30
        };

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(builder.ConnectionString)
            .Options;

        return (new ApplicationDbContext(options), builder.InitialCatalog);
    }

    private static async Task<bool> ColumnExistsAsync(ApplicationDbContext context, string tableName, string columnName)
    {
        var count = await context.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(1) AS Value FROM sys.columns c INNER JOIN sys.tables t ON c.object_id = t.object_id WHERE t.name = {0} AND c.name = {1}",
                tableName,
                columnName)
            .SingleAsync();

        return count > 0;
    }

    private static async Task<bool> MigrationRecordedAsync(ApplicationDbContext context, string migrationId)
    {
        var count = await context.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(1) AS Value FROM [__EFMigrationsHistory] WHERE [MigrationId] = {0}",
                migrationId)
            .SingleAsync();

        return count > 0;
    }

    [Fact]
    public async Task LegacyDevelopState_WhenMigrateAsyncRuns_SucceedsPreservesColumnAndRecordsMigration()
    {
        // Target legacy scenario:
        // 1. Appointments.CheckedInAt column already exists.
        // 2. __EFMigrationsHistory contains 20261012163335_AddAppointmentCheckedInAt.
        // 3. __EFMigrationsHistory does NOT contain 20261011130000_AddAppointmentCheckIn.
        var (context, _) = CreateIsolatedContext();
        try
        {
            var migrator = context.Database.GetService<IMigrator>();

            // Setup baseline up to the migration immediately preceding the check-in branches
            await migrator.MigrateAsync("20261009210023_AddAppointmentPaymentDetailsAndBackfill");

            // Simulate the develop branch state: column exists and develop migration is recorded
            await context.Database.ExecuteSqlRawAsync(
                "IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Appointments]') AND name = N'CheckedInAt') " +
                "ALTER TABLE [Appointments] ADD [CheckedInAt] datetime2 NULL;");

            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) " +
                "VALUES (N'20261012163335_AddAppointmentCheckedInAt', N'8.0.13');");

            // Verify preconditions
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeFalse();

            // Act: Migrate to latest reconciled state
            var act = () => context.Database.MigrateAsync();
            await act.Should().NotThrowAsync("MigrateAsync must succeed when column already exists from a prior develop branch migration");

            // Assert: Column preserved and both migrations recorded
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeTrue();
        }
        finally
        {
            try { await context.Database.EnsureDeletedAsync(); } catch { }
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task FreshDatabase_WhenMigrateAsyncRuns_SucceedsAndAppliesAllMigrations()
    {
        var (context, _) = CreateIsolatedContext();
        try
        {
            // Act: Migrate fresh database
            var act = () => context.Database.MigrateAsync();
            await act.Should().NotThrowAsync();

            // Assert: Column exists and both migrations are present
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeTrue();
        }
        finally
        {
            try { await context.Database.EnsureDeletedAsync(); } catch { }
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task MainBranchState_WhenMigrateAsyncRuns_SucceedsAndAppliesSecondMigrationIdempotently()
    {
        var (context, _) = CreateIsolatedContext();
        try
        {
            var migrator = context.Database.GetService<IMigrator>();

            // Setup state as deployed from main (through 20261011130000_AddAppointmentCheckIn)
            await migrator.MigrateAsync("20261011130000_AddAppointmentCheckIn");

            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeFalse();

            // Act: Upgrade to latest reconciled state
            var act = () => context.Database.MigrateAsync();
            await act.Should().NotThrowAsync();

            // Assert: Column preserved and second migration now recorded
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeTrue();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeTrue();
        }
        finally
        {
            try { await context.Database.EnsureDeletedAsync(); } catch { }
            await context.DisposeAsync();
        }
    }

    [Fact]
    public async Task Rollback_FromLatestToBeforeCheckInMigrations_DropsColumnSafely()
    {
        var (context, _) = CreateIsolatedContext();
        try
        {
            var migrator = context.Database.GetService<IMigrator>();

            // Start fully migrated
            await context.Database.MigrateAsync();
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeTrue();

            // Act: Rollback to immediately before check-in migrations
            await migrator.MigrateAsync("20261009210023_AddAppointmentPaymentDetailsAndBackfill");

            // Assert: Column is cleanly dropped and neither check-in migration is recorded
            (await ColumnExistsAsync(context, "Appointments", "CheckedInAt")).Should().BeFalse();
            (await MigrationRecordedAsync(context, "20261011130000_AddAppointmentCheckIn")).Should().BeFalse();
            (await MigrationRecordedAsync(context, "20261012163335_AddAppointmentCheckedInAt")).Should().BeFalse();
        }
        finally
        {
            try { await context.Database.EnsureDeletedAsync(); } catch { }
            await context.DisposeAsync();
        }
    }
}
