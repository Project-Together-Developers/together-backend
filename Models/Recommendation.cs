namespace together_api.Models;

public class Recommendation
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = ""; // cinema / concert / theater / bar / sport / other
    public string? ImageUrl { get; set; }
    public string? Link { get; set; }
    public string? Duration { get; set; }       // "1ч 45мин"
    public string? EventLocation { get; set; }  // место проведения
    public string? Language { get; set; }       // язык
    public decimal? Price { get; set; }         // цена, null = не указана
    public bool IsFree { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int CreatedById { get; set; }
    public User? CreatedBy { get; set; }
}
