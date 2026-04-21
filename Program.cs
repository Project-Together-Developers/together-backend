using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Подключаем базу данных
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration
        .GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Создаём таблицы автоматически при старте
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Получить все объявления
app.MapGet("/posts", async (AppDbContext db) =>
    await db.Posts.ToListAsync());

// Создать новое объявление
app.MapPost("/posts", async (AppDbContext db, Post post) =>
{
    db.Posts.Add(post);
    await db.SaveChangesAsync();
    return Results.Created($"/posts/{post.Id}", post);
});

// Получить одно объявление по ID
app.MapGet("/posts/{id}", async (AppDbContext db, int id) =>
{
    var post = await db.Posts.FindAsync(id);
    return post is null ? Results.NotFound() : Results.Ok(post);
});

app.Run();

// Модель объявления
class Post
{
    public int Id { get; set; }
    public string Type { get; set; }
    public string Location { get; set; }
    public string Date { get; set; }
    public string Level { get; set; }
    public string Transport { get; set; }
    public string Description { get; set; }
}

// Контекст базы данных
class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) 
        : base(options) { }
    
    public DbSet<Post> Posts { get; set; }
}