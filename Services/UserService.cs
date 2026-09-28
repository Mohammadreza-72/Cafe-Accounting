using CafeArian.Data;
using CafeArian.Models;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace CafeArian.Services;

public sealed class UserService
{
    private const int Iterations = 210_000;

    public bool HasUsers()
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Users";
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public AppUser CreateInitialAdmin(string username, string password)
    {
        Validate(username, password, "Admin");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var count = connection.CreateCommand();
        count.Transaction = transaction;
        count.CommandText = "SELECT COUNT(*) FROM Users";
        if (Convert.ToInt64(count.ExecuteScalar()) != 0)
            throw new InvalidOperationException("مدیر اولیه قبلاً ساخته شده است.");
        var id = Insert(connection, transaction, username.Trim(), password, "Admin");
        transaction.Commit();
        var admin = new AppUser { Id = id, Username = username.Trim(), Role = "Admin" };
        UserSession.Login(admin);
        return admin;
    }

    public AppUser? SignIn(string username, string password)
    {
        UserSession.Logout();
        var user = Authenticate(username, password);
        if (user is not null) UserSession.Login(user);
        return user;
    }

    public AppUser? Authenticate(string username, string password)
    {
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id,Username,Role,PasswordSalt,PasswordHash,Iterations FROM Users WHERE Username=$name AND IsActive=1";
        cmd.Parameters.AddWithValue("$name", username.Trim());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var salt = (byte[])reader.GetValue(3);
        var expected = (byte[])reader.GetValue(4);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, reader.GetInt32(5), HashAlgorithmName.SHA256, expected.Length);
        if (!CryptographicOperations.FixedTimeEquals(hash, expected)) return null;
        return new AppUser { Id = reader.GetInt64(0), Username = reader.GetString(1), Role = reader.GetString(2) };
    }

    public List<AppUser> All()
    {
        UserSession.Require("Admin");
        using var connection = Database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id,Username,Role FROM Users WHERE IsActive=1 ORDER BY Id";
        using var reader = cmd.ExecuteReader();
        var result = new List<AppUser>();
        while (reader.Read()) result.Add(new AppUser
        {
            Id = reader.GetInt64(0), Username = reader.GetString(1), Role = reader.GetString(2)
        });
        return result;
    }

    public void Add(string username, string password, string role)
    {
        UserSession.Require("Admin");
        Validate(username, password, role);
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var id = Insert(connection, transaction, username.Trim(), password, role);
        Audit(connection, transaction, "UserCreated", id);
        transaction.Commit();
    }

    public void ResetPassword(long id, string password)
    {
        UserSession.Require("Admin");
        ValidatePassword(password);
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "UPDATE Users SET PasswordSalt=$salt,PasswordHash=$hash,Iterations=$iterations WHERE Id=$id AND IsActive=1";
        cmd.Parameters.AddWithValue("$salt", salt);
        cmd.Parameters.AddWithValue("$hash", hash);
        cmd.Parameters.AddWithValue("$iterations", Iterations);
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("کاربر فعال پیدا نشد.");
        Audit(connection, transaction, "UserPasswordReset", id);
        transaction.Commit();
    }

    public void Deactivate(long id)
    {
        UserSession.Require("Admin");
        if (UserSession.Current?.Id == id) throw new InvalidOperationException("حساب در حال استفاده را نمی‌توان غیرفعال کرد.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            UPDATE Users SET IsActive=0 WHERE Id=$id AND IsActive=1
              AND (Role!='Admin' OR (SELECT COUNT(*) FROM Users WHERE Role='Admin' AND IsActive=1)>1);
            """;
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("کاربر پیدا نشد یا آخرین مدیر است.");
        Audit(connection, transaction, "UserDeactivated", id);
        transaction.Commit();
    }

    private static long Insert(SqliteConnection connection, SqliteTransaction transaction,
        string username, string password, string role)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO Users(Username,PasswordSalt,PasswordHash,Iterations,Role)
            VALUES($username,$salt,$hash,$iterations,$role);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$username", username);
        cmd.Parameters.AddWithValue("$salt", salt);
        cmd.Parameters.AddWithValue("$hash", hash);
        cmd.Parameters.AddWithValue("$iterations", Iterations);
        cmd.Parameters.AddWithValue("$role", role);
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static void Audit(SqliteConnection connection, SqliteTransaction transaction, string action, long id)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO AuditLog(Action,ReferenceType,ReferenceId) VALUES($action,'User',$id)";
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static void Validate(string username, string password, string role)
    {
        username = username.Trim();
        if (username.Length is < 3 or > 40 || username.Any(char.IsWhiteSpace) ||
            role is not ("Admin" or "Cashier" or "Inventory"))
            throw new InvalidOperationException("نام کاربری یا نقش معتبر نیست.");
        ValidatePassword(password);
    }

    private static void ValidatePassword(string password)
    {
        if (password.Length < 12 || password.Length > 200)
            throw new InvalidOperationException("رمز باید دست‌کم ۱۲ نویسه داشته باشد.");
    }
}
