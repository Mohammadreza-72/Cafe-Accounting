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
            LoginTitle.Text = "ساخت حساب مدیر اولیه";
            LoginHint.Text = "در اولین اجرا یک نام کاربری و رمز دست‌کم ۱۲ نویسه‌ای بسازید. این رمز پیش‌فرض ندارد.";
            SubmitButton.Content = "ساخت مدیر و ورود";
        }
        else
        {
            ConfirmLabel.Visibility = Visibility.Collapsed;
            ConfirmInput.Visibility = Visibility.Collapsed;
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
        catch (Exception ex) { ErrorText.Text = ex.Message; }
    }
}
