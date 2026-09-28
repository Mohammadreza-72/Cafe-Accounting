using System.Windows.Controls;
using System.Windows.Documents;
using System.Printing;

namespace CafeArian.Services;

public enum PrintJobKind { Receipt, Label }

public interface IPrintBackend
{
    bool Print(FlowDocument document, string jobName, PrintJobKind kind);
}

public sealed class WindowsPrintBackend : IPrintBackend
{
    public bool Print(FlowDocument document, string jobName, PrintJobKind kind)
    {
        var dialog = new PrintDialog();
        var settings = new PrintSettingsService().Load();
        var configuredName = kind == PrintJobKind.Receipt ? settings.ReceiptPrinter : settings.LabelPrinter;
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            try
            {
                using var server = new LocalPrintServer();
                var queue = server.GetPrintQueues().FirstOrDefault(x =>
                    string.Equals(x.Name, configuredName, StringComparison.OrdinalIgnoreCase));
                if (queue is not null) dialog.PrintQueue = queue;
            }
            catch (PrintSystemException)
            {
                // A saved printer can be disconnected; the dialog still lets the user select one.
            }
        }
        if (dialog.ShowDialog() != true) return false;
        var widthMm = kind == PrintJobKind.Receipt ? settings.ReceiptWidthMm : settings.LabelWidthMm;
        document.PageWidth = Math.Min(dialog.PrintableAreaWidth, widthMm * 96.0 / 25.4);
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, jobName);
        return true;
    }
}
