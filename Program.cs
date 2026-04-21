var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Временная база данных (список в памяти)
var posts = new List<Post>();

// Получить все объявления
app.MapGet("/posts", () => posts);

// Создать новое объявление
app.MapPost("/posts", (Post post) =>
{
    post.Id = posts.Count + 1;
    posts.Add(post);
    return Results.Created($"/posts/{post.Id}", post);
});

// Получить одно объявление по ID
app.MapGet("/posts/{id}", (int id) =>
{
    var post = posts.FirstOrDefault(p => p.Id == id);
    return post is null ? Results.NotFound() : Results.Ok(post);
});

app.Run();

// Модель объявления
class Post
{
    public int Id { get; set; }
    public string Type { get; set; }       // поход, лыжи, сноуборд
    public string Location { get; set; }   // куда едем
    public string Date { get; set; }       // когда
    public string Level { get; set; }      // новичок, средний, профи
    public string Transport { get; set; }  // есть машина / нужна машина
    public string Description { get; set; }
}