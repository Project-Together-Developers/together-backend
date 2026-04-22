namespace together_api.DTOs;

public record PostCreateDto(
    string Activity,
    string Location,
    string? Region,
    DateTime DateFrom,
    DateTime DateTo,
    string Difficulty,
    int TotalSpots,
    string Transport,
    string? Budget,
    string? Description
);
