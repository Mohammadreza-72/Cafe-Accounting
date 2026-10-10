using CafeArian.Services;
using System.Windows;

namespace CafeArian;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        WindowLayout.Attach(this);
        UsernameBox.Focus();
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        try { new UserService().SaveLocalProfile(UsernameBox.Text); DialogResult = true; }
        catch (InvalidOperationException ex) { ErrorText.Text = ex.Message; }
        catch (Exception ex)
        {
            var entry = DiagnosticsService.Record(ex, "ثبت نام کاربر");
            ErrorText.Text = $"نام ذخیره نشد. شناسه خطا: {entry.Id}";
        }
    }
}
