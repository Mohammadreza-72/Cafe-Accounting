using CafeArian.Data;
using CafeArian.Models;

namespace CafeArian.Services;

public sealed class UserService
{
    public AppUser? StartLocalSession()
    {
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT UserId,DisplayName FROM LocalProfile WHERE Id=1";
        using var reader = cmd.ExecuteReader();
        AppUser? user = reader.Read() ? new AppUser { Id = reader.GetInt64(0), Username = reader.GetString(1), Role = "Admin" } : null;
        reader.Close();
        if (user is null)
        {
            // Keep existing identities and historical foreign keys intact during upgrade.
            cmd.CommandText = "SELECT Id,Username FROM Users ORDER BY (Role='Admin' AND IsActive=1) DESC,Id LIMIT 1";
            using var legacy = cmd.ExecuteReader();
            if (legacy.Read()) user = new AppUser { Id = legacy.GetInt64(0), Username = legacy.GetString(1), Role = "Admin" };
            legacy.Close();
            if (user is not null)
            {
                cmd.CommandText = "INSERT INTO LocalProfile(Id,UserId,DisplayName) VALUES(1,$id,$name)";
                cmd.Parameters.AddWithValue("$id", user.Id);
                cmd.Parameters.AddWithValue("$name", user.Username);
                cmd.ExecuteNonQuery();
            }
        }
        transaction.Commit();
        UserSession.Logout();
        if (user is not null) UserSession.Login(user);
        return user;
    }

    public AppUser SaveLocalProfile(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80 || name.Any(char.IsControl))
            throw new InvalidOperationException("نام کاربر را بین ۱ تا ۸۰ نویسه وارد کنید.");
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT UserId FROM LocalProfile WHERE Id=1";
        var existing = cmd.ExecuteScalar();
        long id;
        if (existing is null)
        {
            // Credential columns remain only for compatibility with old database schemas.
            cmd.CommandText = """
                INSERT INTO Users(Username,PasswordSalt,PasswordHash,Iterations,Role)
                VALUES($key,X'',X'',1,'Admin'); SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$key", "local-" + Guid.NewGuid().ToString("N"));
            id = Convert.ToInt64(cmd.ExecuteScalar());
        }
        else id = Convert.ToInt64(existing);
        cmd.CommandText = """
            INSERT INTO LocalProfile(Id,UserId,DisplayName) VALUES(1,$id,$name)
            ON CONFLICT(Id) DO UPDATE SET DisplayName=excluded.DisplayName;
            INSERT INTO AuditLog(Action,ReferenceType,ReferenceId,UserId)
            VALUES('LocalProfileSaved','User',$id,$id);
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
        transaction.Commit();
        var user = new AppUser { Id = id, Username = name, Role = "Admin" };
        UserSession.Login(user);
        return user;
    }
}
