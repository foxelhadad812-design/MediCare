using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MediCare.Web.Infrastructure;

public static class SecurityHeadersMiddleware
{
    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        // Standard OWASP defensive security headers
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["X-XSS-Protection"] = "0";

        await next(context);
    }
}

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(SecurityHeadersMiddleware.InvokeAsync);
    }
}
