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
    }
}
