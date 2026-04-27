namespace together_api.Models;

public class AttendanceConfirmation
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public int ConfirmerUserId { get; set; }
    public int TargetUserId { get; set; }
    public bool WasPresent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? Confirmer { get; set; }
    public User? Target { get; set; }
}
