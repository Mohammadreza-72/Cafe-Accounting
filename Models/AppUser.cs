namespace CafeArian.Models;

public sealed class AppUser
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
    public string Role { get; init; } = "";
    public string RoleName => Role switch { "Admin" => "مدیر", "Cashier" => "صندوق‌دار", _ => "انباردار" };
}
