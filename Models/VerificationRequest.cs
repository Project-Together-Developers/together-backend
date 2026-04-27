namespace together_api.Models;

public class VerificationRequest
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    // Паспортные данные
    public string FullName { get; set; } = "";
    public string PassportNumber { get; set; } = "";
    public string BirthDate { get; set; } = "";

    // Фото (URL из /uploads/)
    public string PassportPhotoUrl { get; set; } = "";
    public string SelfiePhotoUrl { get; set; } = "";

    // pending / approved / rejected
    public string Status { get; set; } = "pending";
    public string? RejectionReason { get; set; }

    public int? ReviewedById { get; set; }
    public User? ReviewedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
