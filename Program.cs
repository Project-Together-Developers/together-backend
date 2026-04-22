using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
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
    if (await db.Users.AnyAsync(u => u.Email == dto.Email))
        return Results.Conflict("Email already taken");

    if (await db.Users.AnyAsync(u => u.Username == dto.Username))
        return Results.Conflict("Username already taken");

    var user = new User
    {
        Name = dto.Name,
        Username = dto.Username,
        Email = dto.Email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
        Location = dto.Location ?? ""
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/users/{user.Id}", new { user.Id, user.Name, user.Username, user.Email });
});

app.MapPost("/auth/login", async (AppDbContext db, LoginDto dto) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
    if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        return Results.Unauthorized();

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
        user = new { user.Id, user.Name, user.Username, user.Email, user.Location }
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
        PostId = dto.PostId,
        Rating = dto.Rating,
        Text = dto.Text
    };
    db.Reviews.Add(review);
    await db.SaveChangesAsync();

    return Results.Created($"/reviews/{review.Id}", new { review.Id });
}).RequireAuthorization();

// ── Profile ───────────────────────────────────────────────────────────────────

app.MapGet("/users/{id:int}", async (AppDbContext db, int id) =>
{
    var user = await db.Users
        .Include(u => u.ReviewsReceived).ThenInclude(r => r.FromUser)
        .FirstOrDefaultAsync(u => u.Id == id);

    if (user is null) return Results.NotFound();

    var avgRating = user.ReviewsReceived.Count > 0
        ? user.ReviewsReceived.Average(r => r.Rating)
        : 0.0;

    return Results.Ok(new
    {
        user.Id, user.Name, user.Username, user.Location, user.CreatedAt,
        Rating = Math.Round(avgRating, 1),
        ReviewCount = user.ReviewsReceived.Count,
        Reviews = user.ReviewsReceived.OrderByDescending(r => r.CreatedAt).Take(10).Select(r => new
        {
            r.Id, r.Rating, r.Text, r.CreatedAt,
            From = new { r.FromUser!.Id, r.FromUser.Name, r.FromUser.Username }
        })
    });
});

app.Run();
