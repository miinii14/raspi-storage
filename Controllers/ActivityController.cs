using Microsoft.AspNetCore.Mvc;
using RaspiStorage.Models;
using RaspiStorage.Services;

namespace RaspiStorage.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ActivityController : ControllerBase
{
    private readonly IActivityService _activityService;

    public ActivityController(IActivityService activityService)
    {
        _activityService = activityService;
    }

    private User GetCurrentUser() => (User)HttpContext.Items["CurrentUser"]!;

    [HttpGet]
    public async Task<IActionResult> GetRecent([FromQuery] int? limit)
    {
        var ownerId = GetCurrentUser().Id;
        return Ok(await _activityService.GetRecentAsync(ownerId, limit));
    }
}
