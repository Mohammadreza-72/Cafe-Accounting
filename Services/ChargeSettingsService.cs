using CafeArian.Data;
using CafeArian.Models;
using Microsoft.Data.Sqlite;

namespace CafeArian.Services;

public sealed class ChargeSettingsService
{
    public ChargeSettings Load()
    {
        using var connection = Database.OpenConnection();
        return Load(connection, null);
    }

    internal static ChargeSettings Load(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT TaxMode,TaxValue,TaxBase,FeeMode,FeeValue,FeeBase,RoundingMode
            FROM SaleChargeSettings WHERE Id=1;
            """;
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("تنظیمات مالیات و کارمزد پیدا نشد.");
        return new ChargeSettings
        {
            TaxMode = reader.GetString(0), TaxValue = Convert.ToDecimal(reader.GetValue(1)),
            TaxBase = reader.GetString(2), FeeMode = reader.GetString(3),
            FeeValue = Convert.ToDecimal(reader.GetValue(4)), FeeBase = reader.GetString(5),
            RoundingMode = reader.GetString(6)
        };
    }

    public void Save(ChargeSettings settings)
    {
        Validate(settings);
        using var connection = Database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            UPDATE SaleChargeSettings SET TaxMode=$taxMode,TaxValue=$taxValue,TaxBase=$taxBase,
                FeeMode=$feeMode,FeeValue=$feeValue,FeeBase=$feeBase,RoundingMode=$rounding WHERE Id=1;
            """;
        cmd.Parameters.AddWithValue("$taxMode", settings.TaxMode);
        cmd.Parameters.AddWithValue("$taxValue", (long)settings.TaxValue);
        cmd.Parameters.AddWithValue("$taxBase", settings.TaxBase);
        cmd.Parameters.AddWithValue("$feeMode", settings.FeeMode);
        cmd.Parameters.AddWithValue("$feeValue", (long)settings.FeeValue);
        cmd.Parameters.AddWithValue("$feeBase", settings.FeeBase);
        cmd.Parameters.AddWithValue("$rounding", settings.RoundingMode);
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("ذخیرهٔ تنظیمات انجام نشد.");
        cmd.CommandText = "INSERT INTO AuditLog(Action,ReferenceType,ReferenceId,Details) VALUES('ChargeSettingsUpdated','Settings',1,$details)";
        cmd.Parameters.AddWithValue("$details", $"مالیات {settings.TaxMode}/{settings.TaxValue}؛ کارمزد {settings.FeeMode}/{settings.FeeValue}");
        cmd.ExecuteNonQuery();
        transaction.Commit();
    }

    public ChargeQuote Quote(decimal subtotal, decimal discount, bool applyTax, bool applyFee)
    {
        using var connection = Database.OpenConnection();
        return Resolve(connection, null, subtotal, discount, applyTax, applyFee);
    }

    internal static ChargeQuote Resolve(SqliteConnection connection, SqliteTransaction? transaction,
        decimal subtotal, decimal discount, bool applyTax, bool applyFee)
    {
        if (subtotal < 0 || discount < 0 || discount > subtotal)
            throw new InvalidOperationException("جمع و تخفیف فاکتور معتبر نیست.");
        var settings = Load(connection, transaction);
        Validate(settings);
        decimal Amount(string mode, decimal value, string basis)
        {
            if (mode == "Disabled") return 0;
            if (mode == "Fixed") return value;
            var baseAmount = basis == "BeforeDiscount" ? subtotal : subtotal - discount;
            var raw = baseAmount * value / 100;
            return settings.RoundingMode switch
            {
                "Floor" => Math.Floor(raw),
                "Ceiling" => Math.Ceiling(raw),
                _ => Math.Round(raw, 0, MidpointRounding.AwayFromZero)
            };
        }
        return new ChargeQuote(
            applyTax ? Amount(settings.TaxMode, settings.TaxValue, settings.TaxBase) : 0,
            applyFee ? Amount(settings.FeeMode, settings.FeeValue, settings.FeeBase) : 0);
    }

    private static void Validate(ChargeSettings settings)
    {
        static bool Mode(string value) => value is "Disabled" or "Percent" or "Fixed";
        static bool Basis(string value) => value is "BeforeDiscount" or "AfterDiscount";
        if (!Mode(settings.TaxMode) || !Mode(settings.FeeMode) ||
            !Basis(settings.TaxBase) || !Basis(settings.FeeBase) ||
            settings.RoundingMode is not ("HalfUp" or "Floor" or "Ceiling") ||
            settings.TaxValue < 0 || settings.FeeValue < 0 ||
            settings.TaxValue != decimal.Truncate(settings.TaxValue) ||
            settings.FeeValue != decimal.Truncate(settings.FeeValue) ||
            settings.TaxValue > long.MaxValue || settings.FeeValue > long.MaxValue ||
            settings.TaxMode == "Percent" && settings.TaxValue > 100 ||
            settings.FeeMode == "Percent" && settings.FeeValue > 100)
            throw new InvalidOperationException("تنظیمات مالیات یا کارمزد معتبر نیست.");
    }
}
