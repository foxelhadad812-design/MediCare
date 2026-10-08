using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace MediCare.Web.Controllers.Api;

[ApiController]
[Route("api/chatbot")]
[AllowAnonymous]
[EnableRateLimiting("ChatbotRateLimitPolicy")]
public class ChatbotApiController : ControllerBase
{
    private readonly IChatbotService _chatbotService;

    public ChatbotApiController(IChatbotService chatbotService)
    {
        _chatbotService = chatbotService;
    }

    [HttpPost("message")]
    public async Task<IActionResult> SendMessage([FromBody] ChatbotRequestDto request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { success = false, error = "Message cannot be empty." });
        }

        var result = await _chatbotService.ProcessMessageAsync(request);
        return Ok(new
        {
            success = true,
            data = result
        });
    }
}
