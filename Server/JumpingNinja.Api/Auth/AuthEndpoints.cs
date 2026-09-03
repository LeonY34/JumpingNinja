using System.Security.Claims;
using JumpingNinja.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace JumpingNinja.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth");

        group.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .RequireRateLimiting("register");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting("login");

        group.MapGet("/me", GetMeAsync)
            .RequireAuthorization(new AuthorizeAttribute());

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        AuthCredentials? input,
        UserManager<ApplicationUser> userManager,
        JwtTokenService tokenService)
    {
        var validationError = ValidateCredentials(input, out var username, out var password);
        if (validationError is not null)
        {
            return Results.BadRequest(validationError);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = username
        };

        IdentityResult result;
        try
        {
            result = await userManager.CreateAsync(user, password!);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new ErrorResponse(
                "username_taken",
                "That username is already in use.",
                "username"));
        }

        if (!result.Succeeded)
        {
            if (result.Errors.Any(error => error.Code.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)))
            {
                return Results.Conflict(new ErrorResponse(
                    "username_taken",
                    "That username is already in use.",
                    "username"));
            }

            return Results.BadRequest(new ErrorResponse(
                "validation_error",
                "The account details are invalid."));
        }

        return Results.Created(
            "/api/v1/auth/me",
            CreateAuthResponse(user, tokenService));
    }

    private static async Task<IResult> LoginAsync(
        AuthCredentials? input,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        JwtTokenService tokenService)
    {
        var validationError = ValidateCredentials(input, out var username, out var password);
        if (validationError is not null)
        {
            return Results.BadRequest(validationError);
        }

        var user = await userManager.FindByNameAsync(username!);
        if (user is null)
        {
            return InvalidCredentials();
        }

        var passwordValid = await signInManager.CheckPasswordSignInAsync(
            user,
            password!,
            lockoutOnFailure: true);
        if (!passwordValid.Succeeded)
        {
            return InvalidCredentials();
        }

        return Results.Ok(CreateAuthResponse(user, tokenService));
    }

    private static async Task<IResult> GetMeAsync(
        ClaimsPrincipal principal,
        UserManager<ApplicationUser> userManager)
    {
        var userIdClaim = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return UnauthorizedResponse();
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null
            ? UnauthorizedResponse()
            : Results.Ok(ToUserResponse(user));
    }

    private static ErrorResponse? ValidateCredentials(
        AuthCredentials? input,
        out string? username,
        out string? password)
    {
        username = AuthRules.NormalizeUsername(input?.Username);
        password = input?.Password;

        var usernameError = AuthRules.ValidateUsername(input?.Username);
        if (usernameError is not null)
        {
            return new ErrorResponse("validation_error", usernameError, "username");
        }

        var passwordError = AuthRules.ValidatePassword(password);
        return passwordError is null
            ? null
            : new ErrorResponse("validation_error", passwordError, "password");
    }

    private static IResult InvalidCredentials() =>
        Results.Json(
            new ErrorResponse("invalid_credentials", "Username or password is incorrect."),
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult UnauthorizedResponse() =>
        Results.Json(
            new ErrorResponse("unauthorized", "Authentication is required."),
            statusCode: StatusCodes.Status401Unauthorized);

    private static AuthResponse CreateAuthResponse(
        ApplicationUser user,
        JwtTokenService tokenService)
    {
        var accessToken = tokenService.Create(user);
        return new AuthResponse(
            ToUserResponse(user),
            accessToken.Value,
            accessToken.ExpiresAt);
    }

    private static AuthUserResponse ToUserResponse(ApplicationUser user) =>
        new(user.Id, user.UserName ?? string.Empty);
}
