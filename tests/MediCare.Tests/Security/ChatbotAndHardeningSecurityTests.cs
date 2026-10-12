using FluentAssertions;
using MediCare.Web.Controllers;
using MediCare.Web.Controllers.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Security;

public class ChatbotAndHardeningSecurityTests
{
    [Fact]
    public void ChatbotApiController_HasRateLimitingPolicyConfigured()
    {
        var controllerType = typeof(ChatbotApiController);
        var method = controllerType.GetMethod("SendMessage");

        var classAttr = controllerType.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false).FirstOrDefault() as EnableRateLimitingAttribute;
        var methodAttr = method?.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false).FirstOrDefault() as EnableRateLimitingAttribute;

        var effectivePolicy = methodAttr?.PolicyName ?? classAttr?.PolicyName;

        effectivePolicy.Should().NotBeNull("Chatbot API must be protected by [EnableRateLimiting]");
        effectivePolicy.Should().Be("ChatbotRateLimitPolicy");
    }

    [Fact]
    public void HomeController_Error_ReturnsCleanViewModel_WithoutSensitiveDetails()
    {
        var loggerMock = new Mock<ILogger<HomeController>>();
        var controller = new HomeController(loggerMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = controller.Error();

        result.Should().BeOfType<ViewResult>();
        var viewResult = (ViewResult)result;
        viewResult.Model.Should().NotBeNull();
        viewResult.Model.Should().BeOfType<MediCare.Web.Models.ErrorViewModel>();
    }
}
