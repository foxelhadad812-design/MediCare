using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Data.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace MediCare.Tests.Security;

public class AdminSeedingSecurityTests
{
    private ServiceProvider BuildSeedServiceProvider(string environmentName, Dictionary<string, string?> configValues)
    {
        var services = new ServiceCollection();

        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(dbName));

        services.AddIdentity<ApplicationUser, IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddLogging();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        var hostEnvMock = new Mock<IHostEnvironment>();
        hostEnvMock.Setup(h => h.EnvironmentName).Returns(environmentName);
        services.AddSingleton(hostEnvMock.Object);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task DbInitializer_InProduction_WhenAdminPasswordMissing_ThrowsCriticalException()
    {
        // Arrange: Production environment without Seed:AdminPassword
        var config = new Dictionary<string, string?>
        {
            ["Seed:OtherKey"] = "val"
        };
        using var sp = BuildSeedServiceProvider("Production", config);

        // Act: Must fail startup
        var act = async () => await DbInitializer.InitializeAsync(sp);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Critical Security Failure*");
    }

    [Fact]
    public async Task DbInitializer_InProduction_WhenAdminPasswordConfigured_SeedsAdminSuccessfully()
    {
        // Arrange: Production environment with a strong secret
        var config = new Dictionary<string, string?>
        {
            ["Seed:AdminPassword"] = "SuperSecureAdm!n2026"
        };
        using var sp = BuildSeedServiceProvider("Production", config);

        // Act
        await DbInitializer.InitializeAsync(sp);

        // Assert
        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync("admin@medicare.com");
        admin.Should().NotBeNull();
        admin!.Email.Should().Be("admin@medicare.com");

        var roles = await userManager.GetRolesAsync(admin);
        roles.Should().Contain("Admin");
    }

    [Fact]
    public async Task DbInitializer_InDevelopment_WhenAdminPasswordMissing_DoesNotThrowAndSeedsAdmin()
    {
        // Arrange: Development mode can fall back to dev password
        var config = new Dictionary<string, string?>
        {
            ["Seed:OtherKey"] = "val"
        };
        using var sp = BuildSeedServiceProvider("Development", config);

        // Act
        await DbInitializer.InitializeAsync(sp);

        // Assert
        using var scope = sp.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync("admin@medicare.com");
        admin.Should().NotBeNull();
    }
}
