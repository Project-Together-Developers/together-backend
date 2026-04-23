namespace together_api.DTOs;

public record ReviewCreateDto(
    int ToUserId,
    int? PostId,
    int Rating,
    string Text
);
