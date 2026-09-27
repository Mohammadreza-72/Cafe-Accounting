using System.Windows.Controls;
using System.Windows.Documents;

namespace CafeArian.Services;

public enum PrintJobKind { Receipt, Label }

public interface IPrintBackend
{
    void Print(FlowDocument document, string jobName, PrintJobKind kind);
}

public sealed class WindowsPrintBackend : IPrintBackend
{
    public void Print(FlowDocument document, string jobName, PrintJobKind kind)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        document.PageWidth = dialog.PrintableAreaWidth;
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, jobName);
    }
}
