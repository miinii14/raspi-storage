using RaspiStorage.Data;
using RaspiStorage.DTO;

namespace RaspiStorage.Services;

public interface IActivityService
{
    Task<IEnumerable<ActivityDto>> GetRecentAsync(Guid ownerId, int? limit);
}

public class ActivityService : IActivityService
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    private readonly IActivityRepository _activityRepository;

    public ActivityService(IActivityRepository activityRepository)
    {
        _activityRepository = activityRepository;
    }

    public async Task<IEnumerable<ActivityDto>> GetRecentAsync(Guid ownerId, int? limit)
    {
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var activity = await _activityRepository.GetByOwnerAsync(ownerId, take);
        return activity.Select(ActivityDto.FromModel);
    }
}
