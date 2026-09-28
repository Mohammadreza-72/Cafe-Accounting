using CafeArian.Models;

namespace CafeArian.Services;

public static class UserSession
{
    public static AppUser? Current { get; private set; }

    internal static void Login(AppUser user) => Current = user;
    public static void Logout() => Current = null;

    public static void Require(params string[] roles)
    {
        if (Current is null || !roles.Contains(Current.Role))
            throw new InvalidOperationException("برای این عملیات دسترسی ندارید.");
    }
}
