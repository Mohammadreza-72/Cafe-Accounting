using CafeArian.Models;
using CafeArian.Services;
using System.Windows;

namespace CafeArian;

public partial class LoginWindow : Window
{
    private readonly UserService _users = new();
    private readonly bool _setup;
    public AppUser? User { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        _setup = !_users.HasUsers();
        if (_setup)
        {
            Title = "راه‌اندازی اولیه | کافه آرین";
            LoginTitle.Text = "حساب مدیر خود را بسازید";
            LoginHint.Text = "این اولین اجرای برنامه است. شناسه ورود (ID) و رمز دلخواه خود را بسازید. برنامه شناسه یا رمز پیش‌فرض ندارد.";
            SubmitButton.Content = "ساخت حساب و شروع کار";
        }
        else
        {
            LoginHint.Text = "این دستگاه قبلاً راه‌اندازی شده است. شناسه ورود و رمزی را که ساخته‌اید وارد کنید.";
            PasswordHint.Visibility = Visibility.Collapsed;
            ConfirmLabel.Visibility = Visibility.Collapsed;
            ConfirmInput.Visibility = Visibility.Collapsed;
            Height = 420;
        }
        UsernameBox.Focus();
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_setup)
            {
                if (PasswordInput.Password != ConfirmInput.Password)
                    throw new InvalidOperationException("تکرار رمز با رمز عبور یکسان نیست.");
                User = _users.CreateInitialAdmin(UsernameBox.Text, PasswordInput.Password);
            }
            else
            {
                User = _users.SignIn(UsernameBox.Text, PasswordInput.Password);
                if (User is null) throw new InvalidOperationException("نام کاربری یا رمز عبور درست نیست.");
            }
            PasswordInput.Clear(); ConfirmInput.Clear();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            if (ex is InvalidOperationException) ErrorText.Text = ex.Message;
            else
            {
                var entry = DiagnosticsService.Record(ex, "ورود به برنامه");
                ErrorText.Text = $"ورود انجام نشد. شناسه خطا: {entry.Id}";
            }
        }
    }
}
