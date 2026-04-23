namespace together_api.DTOs;

public record RecommendationCreateDto(
    string Title,
    string Description,
    string Category,
    string? ImageUrl,
    string? Link,
    string? Duration,
    string? EventLocation,
    string? Language,
    decimal? Price,
    bool IsFree
);
