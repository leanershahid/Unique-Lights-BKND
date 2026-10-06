namespace UniqueLights.Api.Authentication;

public static class Roles
{
    public const string Customer = "Customer";
    public const string Admin = "Admin";

    // Fixed ids so roles can be seeded through HasData and referenced deterministically.
    public static readonly Guid CustomerId = Guid.Parse("6a1f0c2e-0d1b-4f3a-9c41-0000000000c1");
    public static readonly Guid AdminId = Guid.Parse("6a1f0c2e-0d1b-4f3a-9c41-0000000000a1");
}

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
}

public static class AppClaimTypes
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
}
