using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace AiAgent.ApiService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AiProxyController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public AiProxyController(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        [HttpPost("ask")]
        public async Task<IActionResult> Ask([FromBody] AiProxyRequest request)
        {
            var baseUrl = _configuration["OpenAiService:BaseUrl"];
            if (string.IsNullOrEmpty(baseUrl))
                return BadRequest("OpenAI service URL is not configured.");

            var client = _httpClientFactory.CreateClient();
            var content = new StringContent(JsonSerializer.Serialize(new { prompt = request.Prompt }), Encoding.UTF8, "application/json");
            var response = await client.PostAsync($"{baseUrl}/api/OpenAi/ask", content);
            var responseString = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, responseString);
            return Content(responseString, "application/json");
        }
    }

    public class AiProxyRequest
    {
        public string Prompt { get; set; }
    }
}