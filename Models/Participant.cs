namespace together_api.Models;

public class Participant
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public Post? Post { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    // Статус: pending (ждёт одобрения), approved, rejected
    public string Status { get; set; } = "pending";
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
