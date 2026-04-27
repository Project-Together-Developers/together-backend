namespace together_api.Models;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Location { get; set; } = "";
    public string Bio { get; set; } = "";
    public string Role { get; set; } = "user"; // user / editor / admin / moderation_staff
    public bool IsVerified { get; set; } = false;
    public string? AvatarUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Post> Posts { get; set; } = [];
    public ICollection<Participant> Participations { get; set; } = [];
    public ICollection<Review> ReviewsReceived { get; set; } = [];
    public ICollection<Review> ReviewsGiven { get; set; } = [];
    public ICollection<Friendship> SentFriendRequests { get; set; } = [];
    public ICollection<Friendship> ReceivedFriendRequests { get; set; } = [];
}
