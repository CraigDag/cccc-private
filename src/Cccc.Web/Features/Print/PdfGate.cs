using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Cccc.Web.Features.Print;

/// <summary>
/// Every PDF generation goes through here. QuestPDF loses text when two documents generate at once —
/// a valid PDF, the right page count, and the room simply absent — so generation must never overlap.
/// It takes a document rather than a delegate so a batch inside one acquisition cannot be written.
/// </summary>
public sealed class PdfGate
{
    private readonly Lock _gate = new();

    public byte[] Generate(IDocument document)
    {
        lock (_gate) return document.GeneratePdf();
    }
}
