using Microsoft.EntityFrameworkCore;
using together_api.Models;

namespace together_api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<DirectMessage> DirectMessages => Set<DirectMessage>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<VerificationRequest> VerificationRequests => Set<VerificationRequest>();
    public DbSet<AttendanceConfirmation> AttendanceConfirmations => Set<AttendanceConfirmation>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // Review — два FK на User, нужно указать явно чтобы EF не путался
        model.Entity<Review>()
            .HasOne(r => r.ToUser)
            .WithMany(u => u.ReviewsReceived)
            .HasForeignKey(r => r.ToUserId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<Review>()
            .HasOne(r => r.FromUser)
            .WithMany(u => u.ReviewsGiven)
            .HasForeignKey(r => r.FromUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Уникальный индекс: один пользователь — один отклик на событие
        model.Entity<Participant>()
            .HasIndex(p => new { p.PostId, p.UserId })
            .IsUnique();

        // Friendship — два FK на User
        model.Entity<Friendship>()
            .HasOne(f => f.Requester)
            .WithMany(u => u.SentFriendRequests)
            .HasForeignKey(f => f.RequesterId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<Friendship>()
            .HasOne(f => f.Addressee)
            .WithMany(u => u.ReceivedFriendRequests)
            .HasForeignKey(f => f.AddresseeId)
            .OnDelete(DeleteBehavior.Restrict);

        // Уникальная пара: нельзя отправить два запроса одному человеку
        model.Entity<Friendship>()
            .HasIndex(f => new { f.RequesterId, f.AddresseeId })
            .IsUnique();

        // DirectMessage — два FK на User
        model.Entity<DirectMessage>()
            .HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<DirectMessage>()
            .HasOne(m => m.Receiver)
            .WithMany()
            .HasForeignKey(m => m.ReceiverId)
            .OnDelete(DeleteBehavior.Restrict);

        model.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        model.Entity<VerificationRequest>()
            .HasOne(v => v.User)
            .WithMany()
            .HasForeignKey(v => v.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        model.Entity<VerificationRequest>()
            .HasOne(v => v.ReviewedBy)
            .WithMany()
            .HasForeignKey(v => v.ReviewedById)
            .OnDelete(DeleteBehavior.SetNull);

        model.Entity<AttendanceConfirmation>()
            .HasIndex(a => new { a.PostId, a.ConfirmerUserId, a.TargetUserId })
            .IsUnique();

        model.Entity<AttendanceConfirmation>()
            .HasOne(a => a.Confirmer).WithMany()
            .HasForeignKey(a => a.ConfirmerUserId).OnDelete(DeleteBehavior.Restrict);

        model.Entity<AttendanceConfirmation>()
            .HasOne(a => a.Target).WithMany()
            .HasForeignKey(a => a.TargetUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
