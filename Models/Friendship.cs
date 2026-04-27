namespace together_api.Models;

public class Friendship
{
    public int Id { get; set; }
    public int RequesterId { get; set; }   // кто отправил
    public int AddresseeId { get; set; }   // кому отправили
    public string Status { get; set; } = "pending"; // pending / accepted
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? Requester { get; set; }
    public User? Addressee { get; set; }
}
