namespace together_api.Models;

public class Post
{
    public int Id { get; set; }

    // Тип активности: hiking, skiing, snowboard, trekking
    public string Activity { get; set; } = "";
    public string Location { get; set; } = "";
    public string Region { get; set; } = "";
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }

    // Уровень: beginner, medium, pro
    public string Difficulty { get; set; } = "";
    public int TotalSpots { get; set; }
    public int FilledSpots { get; set; } = 1; // автор уже считается

    // Транспорт: has-seats, need-ride, public
    public string Transport { get; set; } = "";
    public string? Budget { get; set; }
    public string? Description { get; set; }

    // Статус: active, full, past
    public string Status { get; set; } = "active";
    public bool RequiresSafety { get; set; } = false;

    public int AuthorId { get; set; }
    public User? Author { get; set; }

    public ICollection<Participant> Participants { get; set; } = [];
    public ICollection<ChatMessage> Messages { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
