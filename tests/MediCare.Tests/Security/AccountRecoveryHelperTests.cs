using FluentAssertions;
using MediCare.Data.Entities;
using MediCare.Web.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Security;

public class AccountRecoveryHelperTests
{
    private readonly Mock<UserManager<ApplicationUser>> _mockUserManager;
    private readonly Mock<ILogger> _mockLogger;

    public AccountRecoveryHelperTests()
    {
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        _mockUserManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        _mockLogger = new Mock<ILogger>();
    }

    [Fact]
    public async Task UnlockUserAsync_WhenUserHasLockoutEnabled_ClearsLockoutAndResetsAccessCount()
    {
        // Arrange
        const string email = "admin@medicare.com";
        var user = new ApplicationUser
        {
            Id = "admin-id-1",
            Email = email,
            UserName = email,
            LockoutEnabled = true,
            LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15),
            AccessFailedCount = 5
        };

        _mockUserManager.Setup(m => m.FindByEmailAsync(email))
            .ReturnsAsync(user);

        _mockUserManager.Setup(m => m.SetLockoutEndDateAsync(user, null))
            .ReturnsAsync(IdentityResult.Success);

        _mockUserManager.Setup(m => m.ResetAccessFailedCountAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await AccountRecoveryHelper.UnlockUserAsync(_mockUserManager.Object, email, _mockLogger.Object);

        // Assert
        result.Should().BeTrue();
        _mockUserManager.Verify(m => m.SetLockoutEndDateAsync(user, null), Times.Once);
        _mockUserManager.Verify(m => m.ResetAccessFailedCountAsync(user), Times.Once);
    }

    [Fact]
    public async Task UnlockUserAsync_WhenUserHasLockoutDisabled_UpdatesUserDirectly()
    {
        // Arrange
        const string email = "admin@medicare.com";
        var user = new ApplicationUser
        {
            Id = "admin-id-1",
            Email = email,
            UserName = email,
            LockoutEnabled = false,
            LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15),
            AccessFailedCount = 5
        };

        _mockUserManager.Setup(m => m.FindByEmailAsync(email))
            .ReturnsAsync(user);

        _mockUserManager.Setup(m => m.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await AccountRecoveryHelper.UnlockUserAsync(_mockUserManager.Object, email, _mockLogger.Object);

        // Assert
        result.Should().BeTrue();
        user.LockoutEnd.Should().BeNull();
        user.AccessFailedCount.Should().Be(0);
        _mockUserManager.Verify(m => m.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task UnlockUserAsync_WhenUserDoesNotExist_ReturnsFalseAndLogsWarning()
    {
        // Arrange
        const string email = "missing@medicare.com";
        _mockUserManager.Setup(m => m.FindByEmailAsync(email))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await AccountRecoveryHelper.UnlockUserAsync(_mockUserManager.Object, email, _mockLogger.Object);

        // Assert
        result.Should().BeFalse();
        _mockUserManager.Verify(m => m.SetLockoutEndDateAsync(It.IsAny<ApplicationUser>(), It.IsAny<DateTimeOffset?>()), Times.Never);
        _mockUserManager.Verify(m => m.ResetAccessFailedCountAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Fact]
    public async Task UnlockUserAsync_WhenEmailIsEmpty_ReturnsFalseImmediately()
    {
        // Act
        var result = await AccountRecoveryHelper.UnlockUserAsync(_mockUserManager.Object, "   ", _mockLogger.Object);

        // Assert
        result.Should().BeFalse();
        _mockUserManager.Verify(m => m.FindByEmailAsync(It.IsAny<string>()), Times.Never);
    }
}
