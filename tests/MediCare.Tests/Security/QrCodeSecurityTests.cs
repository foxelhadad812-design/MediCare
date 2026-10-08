using FluentAssertions;
using MediCare.Web.Infrastructure;
using MediCare.Web.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace MediCare.Tests.Security;

public class QrCodeSecurityTests
{
    [Fact]
    public void QrCodeService_GeneratesValidPngDataUri_WithoutExternalNetworkCalls()
    {
        // Arrange
        var service = new QrCodeService();
        var payload = "https://medicare.local/Prescriptions/Verify?token=abcdef0123456789abcdef0123456789";

        // Act
        var result = service.GeneratePngDataUri(payload);

        // Assert: Valid PNG Data URI
        result.Should().StartWith("data:image/png;base64,");

        var base64Part = result.Substring("data:image/png;base64,".Length);
        var bytes = Convert.FromBase64String(base64Part);

        // Standard PNG Magic Bytes: 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        byte[] pngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        bytes.Take(8).Should().Equal(pngHeader, "Generated data URI must be a valid PNG image generated in memory");
    }

    [Fact]
    public void PrescriptionPrintView_DoesNotContainExternalQrServerUrl()
    {
        // Arrange: Locate the Print view file by traversing upwards to the solution root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MediCare.sln")))
        {
            dir = dir.Parent;
        }

        var printViewPath = Path.Combine(dir!.FullName, "src", "MediCare.Web", "Views", "Prescriptions", "Print.cshtml");
        File.Exists(printViewPath).Should().BeTrue($"Print.cshtml must exist at {printViewPath}");

        // Act
        var content = File.ReadAllText(printViewPath);

        // Assert: No third-party QR service leaking the prescription verification token
        content.Should().NotContain("api.qrserver.com",
            "Prescription print view must NOT transmit verification tokens to external third-party QR generation services");
        content.Should().Contain("@qrDataUri",
            "Prescription print view must use server-side generated inline QR data URI");
    }

    [Fact]
    public async Task SecurityHeaders_ContentSecurityPolicy_DoesNotContainQrServerDomain()
    {
        // Arrange
        var context = new DefaultHttpContext();
        RequestDelegate next = (ctx) => Task.CompletedTask;

        // Act
        await SecurityHeadersMiddleware.InvokeAsync(context, next);

        // Assert
        var csp = context.Response.Headers["Content-Security-Policy-Report-Only"].ToString();
        csp.Should().NotContain("api.qrserver.com",
            "CSP img-src directive must not allow external qrserver domain now that QR codes are generated locally");
    }

    [Fact]
    public void LayoutView_DoesNotContainExternalAvatarService()
    {
        // Arrange
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MediCare.sln")))
        {
            dir = dir.Parent;
        }

        var layoutViewPath = Path.Combine(dir!.FullName, "src", "MediCare.Web", "Views", "Shared", "_Layout.cshtml");
        File.Exists(layoutViewPath).Should().BeTrue($"_Layout.cshtml must exist at {layoutViewPath}");

        // Act
        var content = File.ReadAllText(layoutViewPath);

        // Assert
        content.Should().NotContain("ui-avatars.com",
            "Layout view must NOT transmit user names to external avatar services like ui-avatars.com");
    }

    [Fact]
    public async Task SecurityHeaders_ContentSecurityPolicy_DoesNotContainUiAvatarsDomain()
    {
        // Arrange
        var context = new DefaultHttpContext();
        RequestDelegate next = (ctx) => Task.CompletedTask;

        // Act
        await SecurityHeadersMiddleware.InvokeAsync(context, next);

        // Assert
        var csp = context.Response.Headers["Content-Security-Policy-Report-Only"].ToString();
        csp.Should().NotContain("ui-avatars.com",
            "CSP img-src directive must not allow ui-avatars.com since user initials are generated locally");
    }
}
