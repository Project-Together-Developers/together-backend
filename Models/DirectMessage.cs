namespace together_api.Models;

public class DirectMessage
{
    public int Id { get; set; }
    public int SenderId { get; set; }
    public int ReceiverId { get; set; }
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? Sender { get; set; }
    public User? Receiver { get; set; }
}
