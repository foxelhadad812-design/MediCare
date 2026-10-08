using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MediCare.Tests.Security;

public class SecurityHeadersTests
{
    [Fact]
    public async Task SecurityHeadersMiddleware_SetsExpectedDefensiveHeaders()
    {
        // Arrange
        var context = new DefaultHttpContext();
        RequestDelegate next = (ctx) =>
        {
            ctx.Response.StatusCode = 200;
            return Task.CompletedTask;
        };

        // Act: Run the security headers middleware delegate
        await MediCare.Web.Infrastructure.SecurityHeadersMiddleware.InvokeAsync(context, next);

        // Assert
        context.Response.Headers.Should().ContainKey("X-Content-Type-Options");
        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");

        context.Response.Headers.Should().ContainKey("X-Frame-Options");
        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("SAMEORIGIN");

        context.Response.Headers.Should().ContainKey("Referrer-Policy");
        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
    }
}
