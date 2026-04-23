using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using together_api.Data;
using together_api.DTOs;
using together_api.Hubs;
using together_api.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var jwtKey = builder.Configuration["Jwt:Key"]!;
var jwtIssuer = builder.Configuration["Jwt:Issuer"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient("gtickets", c =>
{
    c.BaseAddress = new Uri("https://api.gtickets.uz");
    c.DefaultRequestHeaders.Add("Origin", "https://gtickets.uz");
    c.DefaultRequestHeaders.Add("Referer", "https://gtickets.uz/");
    c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36");
    c.DefaultRequestHeaders.Add("Accept", "*/*");
    c.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9");
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.MapOpenApi();
app.MapScalarApiReference();
app.MapHub<TogetherHub>("/hub");

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ── Auth ──────────────────────────────────────────────────────────────────────

app.MapPost("/auth/register", async (AppDbContext db, RegisterDto dto) =>
{
    var email = dto.Email.ToLower().Trim();
    var username = dto.Username.ToLower().Trim();

    if (await db.Users.AnyAsync(u => u.Email == email))
        return Results.Conflict("Эта почта уже используется");

    if (await db.Users.AnyAsync(u => u.Username == username))
        return Results.Conflict("Этот username уже занят");

    var user = new User
    {
        Name = dto.Name.Trim(),
        Username = username,
        Email = email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
        Location = dto.Location?.Trim() ?? ""
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/users/{user.Id}", new { user.Id, user.Name, user.Username, user.Email });
});

app.MapPost("/auth/login", async (AppDbContext db, LoginDto dto) =>
{
    var email = dto.Email.ToLower().Trim();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
    if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        return Results.Json(new { message = "Неверная почта или пароль" }, statusCode: 401);

    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username)
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        claims: claims,
        expires: DateTime.UtcNow.AddDays(30),
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
    );

    return Results.Ok(new
    {
        token = new JwtSecurityTokenHandler().WriteToken(token),
        user = new { user.Id, user.Name, user.Username, user.Email, user.Location, user.Role }
    });
});

// ── Posts ─────────────────────────────────────────────────────────────────────

app.MapGet("/posts", async (AppDbContext db, string? activity, string? difficulty, string? status) =>
{
    var query = db.Posts
        .Include(p => p.Author)
        .AsQueryable();

    if (activity is not null) query = query.Where(p => p.Activity == activity);
    if (difficulty is not null) query = query.Where(p => p.Difficulty == difficulty);
    if (status is not null) query = query.Where(p => p.Status == status);

    var posts = await query.OrderByDescending(p => p.CreatedAt).Select(p => new
    {
        p.Id, p.Activity, p.Location, p.Region, p.DateFrom, p.DateTo,
        p.Difficulty, p.TotalSpots, p.FilledSpots, p.Transport, p.Budget,
        p.Description, p.Status, p.CreatedAt,
        Author = new { p.Author!.Id, p.Author.Name, p.Author.Username }
    }).ToListAsync();

    return Results.Ok(posts);
});

app.MapGet("/posts/{id:int}", async (AppDbContext db, int id) =>
{
    var post = await db.Posts
        .Include(p => p.Author)
        .Include(p => p.Participants).ThenInclude(p => p.User)
        .FirstOrDefaultAsync(p => p.Id == id);

    if (post is null) return Results.NotFound();

    return Results.Ok(new
    {
        post.Id, post.Activity, post.Location, post.Region, post.DateFrom, post.DateTo,
        post.Difficulty, post.TotalSpots, post.FilledSpots, post.Transport, post.Budget,
        post.Description, post.Status, post.CreatedAt,
        Author = new { post.Author!.Id, post.Author.Name, post.Author.Username },
        Participants = post.Participants.Select(p => new
        {
            p.UserId, p.Status, p.JoinedAt,
            User = new { p.User!.Id, p.User.Name, p.User.Username }
        })
    });
});

app.MapPost("/posts", async (AppDbContext db, ClaimsPrincipal user, PostCreateDto dto, IHubContext<TogetherHub> hub) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var author = await db.Users.FindAsync(userId);

    var post = new Post
    {
        Activity = dto.Activity,
        Location = dto.Location,
        Region = dto.Region ?? "",
        DateFrom = dto.DateFrom,
        DateTo = dto.DateTo,
        Difficulty = dto.Difficulty,
        TotalSpots = dto.TotalSpots,
        Transport = dto.Transport,
        Budget = dto.Budget,
        Description = dto.Description,
        AuthorId = userId
    };
    db.Posts.Add(post);
    await db.SaveChangesAsync();

    await hub.Clients.Group("feed").SendAsync("NewPost", new
    {
        post.Id, post.Activity, post.Location, post.Region, post.DateFrom, post.DateTo,
        post.Difficulty, post.TotalSpots, post.FilledSpots, post.Transport, post.Budget,
        post.Description, post.Status, post.CreatedAt,
        Author = new { author!.Id, author.Name, author.Username }
    });

    return Results.Created($"/posts/{post.Id}", new { post.Id });
}).RequireAuthorization();

app.MapPut("/posts/{id:int}", async (AppDbContext db, ClaimsPrincipal user, int id, PostCreateDto dto) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var post = await db.Posts.FindAsync(id);
    if (post is null) return Results.NotFound();
    if (post.AuthorId != userId) return Results.Forbid();

    post.Activity = dto.Activity;
    post.Location = dto.Location;
    post.Region = dto.Region ?? "";
    post.DateFrom = dto.DateFrom;
    post.DateTo = dto.DateTo;
    post.Difficulty = dto.Difficulty;
    post.TotalSpots = dto.TotalSpots;
    post.Transport = dto.Transport;
    post.Budget = dto.Budget;
    post.Description = dto.Description;

    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

app.MapDelete("/posts/{id:int}", async (AppDbContext db, ClaimsPrincipal user, int id, IHubContext<TogetherHub> hub) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var post = await db.Posts.FindAsync(id);
    if (post is null) return Results.NotFound();
    if (post.AuthorId != userId) return Results.Forbid();

    db.Posts.Remove(post);
    await db.SaveChangesAsync();

    await hub.Clients.Group("feed").SendAsync("PostDeleted", id);

    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/posts/{id:int}/close", async (AppDbContext db, ClaimsPrincipal user, int id) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var post = await db.Posts.FindAsync(id);
    if (post is null) return Results.NotFound();
    if (post.AuthorId != userId) return Results.Forbid();
    if (post.Status == "closed") return Results.Conflict("Поездка уже закрыта");

    post.Status = "closed";
    await db.SaveChangesAsync();
    return Results.Ok(new { post.Id, post.Status });
}).RequireAuthorization();

// ── Participants ──────────────────────────────────────────────────────────────

app.MapPost("/posts/{id:int}/join", async (AppDbContext db, ClaimsPrincipal user, int id, IHubContext<TogetherHub> hub) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var post = await db.Posts.FindAsync(id);
    if (post is null) return Results.NotFound();
    if (post.AuthorId == userId) return Results.BadRequest("You are the author");

    var exists = await db.Participants.AnyAsync(p => p.PostId == id && p.UserId == userId);
    if (exists) return Results.Conflict("Already joined");

    var joiner = await db.Users.FindAsync(userId);
    db.Participants.Add(new Participant { PostId = id, UserId = userId });
    await db.SaveChangesAsync();

    await hub.Clients.Group($"post-{id}").SendAsync("ParticipantJoined", new
    {
        UserId = userId,
        Status = "pending",
        User = new { joiner!.Id, joiner.Name, joiner.Username }
    });

    return Results.Ok(new { status = "pending" });
}).RequireAuthorization();

app.MapPut("/posts/{postId:int}/participants/{targetUserId:int}/approve",
    async (AppDbContext db, ClaimsPrincipal user, int postId, int targetUserId, IHubContext<TogetherHub> hub) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var post = await db.Posts.FindAsync(postId);
    if (post is null) return Results.NotFound();
    if (post.AuthorId != userId) return Results.Forbid();

    var participant = await db.Participants
        .FirstOrDefaultAsync(p => p.PostId == postId && p.UserId == targetUserId);
    if (participant is null) return Results.NotFound();

    participant.Status = "approved";
    post.FilledSpots++;
    if (post.FilledSpots >= post.TotalSpots) post.Status = "full";

    await db.SaveChangesAsync();

    await hub.Clients.Group($"post-{postId}").SendAsync("ParticipantUpdated", new
    {
        UserId = targetUserId,
        Status = "approved",
        FilledSpots = post.FilledSpots,
        PostStatus = post.Status
    });

    return Results.Ok(new { status = "approved" });
}).RequireAuthorization();

app.MapPut("/posts/{postId:int}/participants/{targetUserId:int}/reject",
    async (AppDbContext db, ClaimsPrincipal user, int postId, int targetUserId, IHubContext<TogetherHub> hub) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var post = await db.Posts.FindAsync(postId);
    if (post is null) return Results.NotFound();
    if (post.AuthorId != userId) return Results.Forbid();

    var participant = await db.Participants
        .FirstOrDefaultAsync(p => p.PostId == postId && p.UserId == targetUserId);
    if (participant is null) return Results.NotFound();

    participant.Status = "rejected";
    await db.SaveChangesAsync();

    await hub.Clients.Group($"post-{postId}").SendAsync("ParticipantUpdated", new
    {
        UserId = targetUserId,
        Status = "rejected",
        FilledSpots = post.FilledSpots,
        PostStatus = post.Status
    });

    return Results.Ok(new { status = "rejected" });
}).RequireAuthorization();

// ── Chat ──────────────────────────────────────────────────────────────────────

app.MapGet("/posts/{id:int}/messages", async (AppDbContext db, ClaimsPrincipal user, int id) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var post = await db.Posts.FindAsync(id);
    if (post is null) return Results.NotFound();

    var isAuthor = post.AuthorId == userId;
    var isApproved = await db.Participants
        .AnyAsync(p => p.PostId == id && p.UserId == userId && p.Status == "approved");

    if (!isAuthor && !isApproved) return Results.Forbid();

    var messages = await db.ChatMessages
        .Where(m => m.PostId == id)
        .Include(m => m.Author)
        .OrderBy(m => m.CreatedAt)
        .Select(m => new
        {
            m.Id, m.Text, m.CreatedAt,
            Author = new { m.Author!.Id, m.Author.Name, m.Author.Username }
        })
        .ToListAsync();

    return Results.Ok(messages);
}).RequireAuthorization();

app.MapPost("/posts/{id:int}/messages", async (AppDbContext db, ClaimsPrincipal user, int id, SendMessageDto dto, IHubContext<TogetherHub> hub) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var post = await db.Posts.FindAsync(id);
    if (post is null) return Results.NotFound();

    var isAuthor = post.AuthorId == userId;
    var isApproved = await db.Participants
        .AnyAsync(p => p.PostId == id && p.UserId == userId && p.Status == "approved");

    if (!isAuthor && !isApproved) return Results.Forbid();

    var author = await db.Users.FindAsync(userId);
    var message = new ChatMessage { PostId = id, AuthorId = userId, Text = dto.Text };
    db.ChatMessages.Add(message);
    await db.SaveChangesAsync();

    await hub.Clients.Group($"post-{id}").SendAsync("NewMessage", new
    {
        message.Id, message.Text, message.CreatedAt,
        Author = new { author!.Id, author.Name, author.Username }
    });

    return Results.Created($"/posts/{id}/messages/{message.Id}", new { message.Id, message.Text, message.CreatedAt });
}).RequireAuthorization();

// ── Reviews ───────────────────────────────────────────────────────────────────

app.MapGet("/users/{id:int}/reviews", async (AppDbContext db, int id) =>
{
    var reviews = await db.Reviews
        .Where(r => r.ToUserId == id)
        .Include(r => r.FromUser)
        .OrderByDescending(r => r.CreatedAt)
        .Select(r => new
        {
            r.Id, r.Rating, r.Text, r.CreatedAt,
            From = new { r.FromUser!.Id, r.FromUser.Name, r.FromUser.Username }
        })
        .ToListAsync();

    return Results.Ok(reviews);
});

app.MapPost("/reviews", async (AppDbContext db, ClaimsPrincipal user, ReviewCreateDto dto) =>
{
    var fromUserId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    if (fromUserId == dto.ToUserId) return Results.BadRequest("Cannot review yourself");

    var review = new Review
    {
        FromUserId = fromUserId,
        ToUserId = dto.ToUserId,
        PostId = dto.PostId > 0 ? dto.PostId : null,
        Rating = dto.Rating,
        Text = dto.Text
    };
    db.Reviews.Add(review);
    await db.SaveChangesAsync();

    return Results.Created($"/reviews/{review.Id}", new { review.Id });
}).RequireAuthorization();

// ── Profile ───────────────────────────────────────────────────────────────────

app.MapGet("/users/me", async (AppDbContext db, ClaimsPrincipal user) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var me = await db.Users
        .Include(u => u.ReviewsReceived).ThenInclude(r => r.FromUser)
        .FirstOrDefaultAsync(u => u.Id == userId);

    if (me is null) return Results.NotFound();

    var avgRating = me.ReviewsReceived.Count > 0
        ? me.ReviewsReceived.Average(r => r.Rating)
        : 0.0;

    var postsCreated = await db.Posts.CountAsync(p => p.AuthorId == userId);
    var tripsJoined  = await db.Participants.CountAsync(p => p.UserId == userId && p.Status == "approved");
    var totalTrips   = postsCreated + tripsJoined;

    var topActivity = await db.Posts
        .Where(p => p.AuthorId == userId)
        .GroupBy(p => p.Activity)
        .OrderByDescending(g => g.Count())
        .Select(g => g.Key)
        .FirstOrDefaultAsync();

    var since = DateTime.UtcNow.AddMonths(-11);
    var since1st = new DateTime(since.Year, since.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    var postsByMonth = await db.Posts
        .Where(p => p.AuthorId == userId && p.CreatedAt >= since1st)
        .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
        .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
        .ToListAsync();

    var tripsByMonth = await db.Participants
        .Where(p => p.UserId == userId && p.Status == "approved" && p.JoinedAt >= since1st)
        .GroupBy(p => new { p.JoinedAt.Year, p.JoinedAt.Month })
        .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
        .ToListAsync();

    var monthlyActivity = Enumerable.Range(0, 12)
        .Select(i => DateTime.UtcNow.AddMonths(-11 + i))
        .Select(d => new
        {
            Month = $"{d.Year}-{d.Month:D2}",
            Trips = (postsByMonth.FirstOrDefault(x => x.Year == d.Year && x.Month == d.Month)?.Count ?? 0)
                  + (tripsByMonth.FirstOrDefault(x => x.Year == d.Year && x.Month == d.Month)?.Count ?? 0)
        })
        .ToList();

    var ratingBreakdown = Enumerable.Range(1, 5).Select(star => new
    {
        Star = star,
        Count = me.ReviewsReceived.Count(r => r.Rating == star)
    }).ToList();

    var badges = new List<string>();
    if (topActivity is "ski" or "snowboard") badges.Add("Лыжник");
    if (topActivity is "hiking" or "trekking") badges.Add("Турист");
    if (postsCreated >= 3) badges.Add("Организатор");
    if (avgRating >= 4.5 && me.ReviewsReceived.Count >= 3) badges.Add("Надёжный");
    if (totalTrips >= 10) badges.Add("Опытный");

    return Results.Ok(new
    {
        me.Id, me.Name, me.Username, me.Email, me.Location, me.Bio, me.CreatedAt,
        Rating = Math.Round(avgRating, 1),
        ReviewCount = me.ReviewsReceived.Count,
        RatingBreakdown = ratingBreakdown,
        Stats = new { TotalTrips = totalTrips, PostsCreated = postsCreated, TripsJoined = tripsJoined },
        TopActivity = topActivity,
        Badges = badges,
        MonthlyActivity = monthlyActivity
    });
}).RequireAuthorization();

app.MapPut("/users/me", async (AppDbContext db, ClaimsPrincipal user, UpdateProfileDto dto) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var me = await db.Users.FindAsync(userId);
    if (me is null) return Results.NotFound();

    if (dto.Name is not null) me.Name = dto.Name.Trim();
    if (dto.Location is not null) me.Location = dto.Location.Trim();
    if (dto.Bio is not null) me.Bio = dto.Bio.Trim();

    await db.SaveChangesAsync();
    return Results.Ok(new { me.Id, me.Name, me.Username, me.Email, me.Location });
}).RequireAuthorization();

app.MapGet("/users/{id:int}", async (AppDbContext db, int id) =>
{
    var user = await db.Users
        .Include(u => u.ReviewsReceived).ThenInclude(r => r.FromUser)
        .FirstOrDefaultAsync(u => u.Id == id);

    if (user is null) return Results.NotFound();

    var avgRating = user.ReviewsReceived.Count > 0
        ? user.ReviewsReceived.Average(r => r.Rating)
        : 0.0;

    var postsCreated = await db.Posts.CountAsync(p => p.AuthorId == id);
    var tripsJoined  = await db.Participants.CountAsync(p => p.UserId == id && p.Status == "approved");
    var totalTrips   = postsCreated + tripsJoined;

    // Топ активность по своим постам
    var topActivity = await db.Posts
        .Where(p => p.AuthorId == id)
        .GroupBy(p => p.Activity)
        .OrderByDescending(g => g.Count())
        .Select(g => g.Key)
        .FirstOrDefaultAsync();

    // Активность по месяцам за последние 12 месяцев
    var since = DateTime.UtcNow.AddMonths(-11);
    var since1st = new DateTime(since.Year, since.Month, 1, 0, 0, 0, DateTimeKind.Utc);

    var postsByMonth = await db.Posts
        .Where(p => p.AuthorId == id && p.CreatedAt >= since1st)
        .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
        .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
        .ToListAsync();

    var tripsByMonth = await db.Participants
        .Where(p => p.UserId == id && p.Status == "approved" && p.JoinedAt >= since1st)
        .GroupBy(p => new { p.JoinedAt.Year, p.JoinedAt.Month })
        .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
        .ToListAsync();

    var monthlyActivity = Enumerable.Range(0, 12)
        .Select(i => DateTime.UtcNow.AddMonths(-11 + i))
        .Select(d => new
        {
            Month = $"{d.Year}-{d.Month:D2}",
            Trips = (postsByMonth.FirstOrDefault(x => x.Year == d.Year && x.Month == d.Month)?.Count ?? 0)
                  + (tripsByMonth.FirstOrDefault(x => x.Year == d.Year && x.Month == d.Month)?.Count ?? 0)
        })
        .ToList();

    // Распределение рейтингов
    var ratingBreakdown = Enumerable.Range(1, 5).Select(star => new
    {
        Star = star,
        Count = user.ReviewsReceived.Count(r => r.Rating == star)
    }).ToList();

    // Авто-бейджи
    var badges = new List<string>();
    if (topActivity is "ski" or "snowboard") badges.Add("Лыжник");
    if (topActivity is "hiking" or "trekking") badges.Add("Турист");
    if (postsCreated >= 3) badges.Add("Организатор");
    if (avgRating >= 4.5 && user.ReviewsReceived.Count >= 3) badges.Add("Надёжный");
    if (totalTrips >= 10) badges.Add("Опытный");

    return Results.Ok(new
    {
        user.Id, user.Name, user.Username, user.Location, user.Bio, user.CreatedAt,
        Rating = Math.Round(avgRating, 1),
        ReviewCount = user.ReviewsReceived.Count,
        RatingBreakdown = ratingBreakdown,
        Stats = new { TotalTrips = totalTrips, PostsCreated = postsCreated, TripsJoined = tripsJoined },
        TopActivity = topActivity,
        Badges = badges,
        MonthlyActivity = monthlyActivity,
        Reviews = user.ReviewsReceived.OrderByDescending(r => r.CreatedAt).Take(10).Select(r => new
        {
            r.Id, r.Rating, r.Text, r.CreatedAt,
            From = new { r.FromUser!.Id, r.FromUser.Name, r.FromUser.Username }
        })
    });
});

// ── Recommendations ───────────────────────────────────────────────────────────

app.MapGet("/recommendations", async (AppDbContext db, string? category) =>
{
    var query = db.Recommendations.Include(r => r.CreatedBy).AsQueryable();
    if (category is not null) query = query.Where(r => r.Category == category);

    var list = await query.OrderByDescending(r => r.CreatedAt).Select(r => new
    {
        r.Id, r.Title, r.Description, r.Category, r.ImageUrl, r.Link, r.CreatedAt,
        r.Duration, r.EventLocation, r.Language, r.Price, r.IsFree,
        CreatedBy = new { r.CreatedBy!.Id, r.CreatedBy.Name, r.CreatedBy.Username }
    }).ToListAsync();

    return Results.Ok(list);
});

app.MapPost("/upload/image", async (HttpRequest request, IWebHostEnvironment env, ClaimsPrincipal user) =>
{
    if (!user.Identity!.IsAuthenticated) return Results.Unauthorized();
    if (!request.HasFormContentType) return Results.BadRequest("Нужен multipart/form-data");

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("image");
    if (file is null) return Results.BadRequest("Файл не найден");

    const long maxSize = 10 * 1024 * 1024; // 10 MB
    if (file.Length > maxSize) return Results.BadRequest("Файл слишком большой. Максимум 10 МБ");

    var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
    var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    if (!allowed.Contains(ext)) return Results.BadRequest("Только JPG, PNG, WEBP, GIF");

    var uploads = Path.Combine(env.WebRootPath, "uploads");
    Directory.CreateDirectory(uploads);

    var fileName = $"{Guid.NewGuid()}{ext}";
    var path = Path.Combine(uploads, fileName);
    await using var stream = File.Create(path);
    await file.CopyToAsync(stream);

    return Results.Ok(new { url = $"/uploads/{fileName}" });
}).RequireAuthorization();

app.MapPost("/recommendations", async (AppDbContext db, ClaimsPrincipal user, RecommendationCreateDto dto) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var me = await db.Users.FindAsync(userId);
    if (me is null) return Results.NotFound();
    if (me.Role != "editor" && me.Role != "admin") return Results.Forbid();

    var rec = new Recommendation
    {
        Title = dto.Title.Trim(),
        Description = dto.Description.Trim(),
        Category = dto.Category,
        ImageUrl = dto.ImageUrl?.Trim(),
        Link = dto.Link?.Trim(),
        Duration = dto.Duration?.Trim(),
        EventLocation = dto.EventLocation?.Trim(),
        Language = dto.Language?.Trim(),
        Price = dto.Price,
        IsFree = dto.IsFree,
        CreatedById = userId
    };
    db.Recommendations.Add(rec);
    await db.SaveChangesAsync();

    return Results.Created($"/recommendations/{rec.Id}", new { rec.Id });
}).RequireAuthorization();

app.MapPut("/recommendations/{id:int}", async (AppDbContext db, ClaimsPrincipal user, int id, RecommendationCreateDto dto) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var me = await db.Users.FindAsync(userId);
    if (me is null) return Results.NotFound();
    if (me.Role != "editor" && me.Role != "admin") return Results.Forbid();

    var rec = await db.Recommendations.FindAsync(id);
    if (rec is null) return Results.NotFound();
    // editor может редактировать только свои
    if (me.Role == "editor" && rec.CreatedById != userId) return Results.Forbid();

    rec.Title = dto.Title.Trim();
    rec.Description = dto.Description.Trim();
    rec.Category = dto.Category;
    rec.ImageUrl = dto.ImageUrl?.Trim();
    rec.Link = dto.Link?.Trim();
    rec.Duration = dto.Duration?.Trim();
    rec.EventLocation = dto.EventLocation?.Trim();
    rec.Language = dto.Language?.Trim();
    rec.Price = dto.Price;
    rec.IsFree = dto.IsFree;
    await db.SaveChangesAsync();

    return Results.NoContent();
}).RequireAuthorization();

app.MapDelete("/recommendations/{id:int}", async (AppDbContext db, ClaimsPrincipal user, int id) =>
{
    var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var me = await db.Users.FindAsync(userId);
    if (me is null) return Results.NotFound();
    if (me.Role != "editor" && me.Role != "admin") return Results.Forbid();

    var rec = await db.Recommendations.FindAsync(id);
    if (rec is null) return Results.NotFound();
    // editor может удалять только свои
    if (me.Role == "editor" && rec.CreatedById != userId) return Results.Forbid();

    db.Recommendations.Remove(rec);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

// ── GTickets proxy ────────────────────────────────────────────────────────────

app.MapGet("/gtickets/events", async (IHttpClientFactory factory, IMemoryCache cache) =>
{
    if (cache.TryGetValue("gt_events", out object? cachedObj) && cachedObj is string cached)
        return Results.Content(cached!, "application/json");
    var client = factory.CreateClient("gtickets");
    try
    {
        var response = await client.GetAsync("/api/playbill3?aggregatorid=6");
        var content = await response.Content.ReadAsStringAsync();
        cache.Set("gt_events", content, TimeSpan.FromMinutes(20));
        return Results.Content(content, "application/json");
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapGet("/gtickets/theater", async (IHttpClientFactory factory, IMemoryCache cache) =>
{
    if (cache.TryGetValue("gt_theater", out object? cachedObj2) && cachedObj2 is string cached)
        return Results.Content(cached!, "application/json");
    var client = factory.CreateClient("gtickets");
    try
    {
        var response = await client.GetAsync("/api/theaterevents?aggregatorid=6");
        var content = await response.Content.ReadAsStringAsync();
        cache.Set("gt_theater", content, TimeSpan.FromMinutes(20));
        return Results.Content(content, "application/json");
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.Run();
