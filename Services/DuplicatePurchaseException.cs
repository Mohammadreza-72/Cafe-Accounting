namespace CafeArian.Services;

public sealed class DuplicatePurchaseException : InvalidOperationException
{
    public DuplicatePurchaseException() : base("این شماره سند برای تأمین‌کننده قبلاً ثبت شده است. ثبت دوباره، موجودی و بدهی را دوباره افزایش می‌دهد.") { }
}
