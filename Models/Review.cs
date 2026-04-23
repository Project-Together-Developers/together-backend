namespace together_api.Models;

public class Review
{
    public int Id { get; set; }
    public int ToUserId { get; set; }   // кому оставляют отзыв
    public User? ToUser { get; set; }
    public int FromUserId { get; set; } // кто оставляет
    public User? FromUser { get; set; }
    public int? PostId { get; set; }     // по какой поездке (необязательно)
    public Post? Post { get; set; }
    public int Rating { get; set; }     // 1–5
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
