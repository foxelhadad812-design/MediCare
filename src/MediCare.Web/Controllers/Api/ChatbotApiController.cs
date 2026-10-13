using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediCare.Web.Controllers.Api;

[ApiController]
[Route("api/chatbot")]
[AllowAnonymous]
[EnableRateLimiting("ChatbotRateLimitPolicy")]
public class ChatbotApiController : ControllerBase
{
    private readonly IChatbotService _chatbotService;
    private readonly ILogger<ChatbotApiController> _logger;

    public ChatbotApiController(
        IChatbotService chatbotService,
        ILogger<ChatbotApiController>? logger = null)
    {
        _chatbotService = chatbotService;
        _logger = logger ?? NullLogger<ChatbotApiController>.Instance;
    }

    [HttpPost("message")]
    [AllowAnonymous]
    public async Task<IActionResult> SendMessage([FromBody] ChatbotRequestDto request, CancellationToken cancellationToken = default)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new { success = false, error = "Message cannot be empty." });
        }

        if (request.Message.Length > 1000)
        {
            return BadRequest(new { success = false, error = "Message cannot exceed 1000 characters." });
        }

        try
        {
            var result = await _chatbotService.ProcessMessageAsync(request);
            return Ok(new
            {
                success = true,
                data = result
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Chatbot message processing timed out or was canceled by the client.");
            return StatusCode(StatusCodes.Status408RequestTimeout, new
            {
                success = false,
                error = "The request timed out. Please try again."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing chatbot message: {Message}", request.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                success = false,
                error = "An unexpected error occurred while processing your request. Please try again."
            });
        }
    }
}
