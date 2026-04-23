namespace together_api.DTOs;

public record RegisterDto(
    string Name,
    string Username,
    string Email,
    string Password,
    string? Location
);

public record LoginDto(
    string Email,
    string Password
);

public record UpdateProfileDto(
    string? Name,
    string? Location,
    string? Bio
);
