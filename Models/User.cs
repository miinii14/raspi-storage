namespace RaspiStorage.Models;

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public long QuotaBytes { get; set; }
    public long UsedBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}
