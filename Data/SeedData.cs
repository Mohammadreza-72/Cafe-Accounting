namespace CafeArian.Data;

public static class SeedData
{
    public static void Initialize()
    {
        using var connection = Database.OpenConnection();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
            INSERT OR IGNORE INTO Categories(Name) VALUES
            ('قهوه'), ('نوشیدنی'), ('کیک و دسر'), ('غذا');

            INSERT OR IGNORE INTO PaymentMethods(Name) VALUES
            ('نقدی'), ('کارتخوان'), ('کارت به کارت');

            INSERT OR IGNORE INTO ExpenseCategories(Name) VALUES
            ('اجاره'), ('حقوق'), ('آب و برق و گاز'), ('تعمیرات'), ('سایر');
            """;
            cmd.ExecuteNonQuery();
        }

    }
}
