using ChatCRM.Application.Billing.DTOs;
using ChatCRM.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ChatCRM.Infrastructure.Services.Invoices
{
    /// <summary>
    /// QuestPDF implementation of <see cref="IInvoicePdfRenderer"/>. Single-page A4, header +
    /// totals + category breakdown. License initialisation lives in <see cref="EnsureLicensed"/>
    /// — Community license is fine until the workspace clears $1M revenue (Meta-side, not ours).
    /// </summary>
    public sealed class QuestPdfInvoiceRenderer : IInvoicePdfRenderer
    {
        private static int _licensed = 0;

        public QuestPdfInvoiceRenderer()
        {
            EnsureLicensed();
        }

        private static void EnsureLicensed()
        {
            // QuestPDF demands a license type be set before rendering. Setting twice is safe but
            // emits a warning — guard with Interlocked so the first ctor wins.
            if (System.Threading.Interlocked.CompareExchange(ref _licensed, 1, 0) == 0)
            {
                QuestPDF.Settings.License = LicenseType.Community;
            }
        }

        public byte[] Render(InvoiceDetailDto invoice)
        {
            ArgumentNullException.ThrowIfNull(invoice);

            return Document.Create(c =>
            {
                c.Page(page =>
                {
                    page.Margin(36);
                    page.Size(PageSizes.A4);
                    page.PageColor(Colors.White);
                    page.DefaultTextStyle(t => t.FontSize(10).FontColor("#1f2937"));

                    page.Header().Element(ComposeHeader(invoice));
                    page.Content().Element(ComposeContent(invoice));
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.DefaultTextStyle(s => s.FontSize(9).FontColor(Colors.Grey.Medium));
                        text.Span("Invoice ");
                        text.Span(invoice.Number).SemiBold();
                        text.Span(" · ");
                        text.Span(invoice.PeriodStartUtc.ToString("MMMM yyyy"));
                        text.Span("    Page ");
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        private static Action<IContainer> ComposeHeader(InvoiceDetailDto invoice) => container =>
        {
            container.Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("ChatCRM").FontSize(20).Bold().FontColor("#0f172a");
                    col.Item().Text("WhatsApp Business CRM").FontSize(10).FontColor(Colors.Grey.Medium);
                });
                row.RelativeItem().AlignRight().Column(col =>
                {
                    col.Item().Text("Invoice").FontSize(20).Bold();
                    col.Item().PaddingTop(2).Text(invoice.Number).FontSize(13).SemiBold();
                    col.Item().Text(invoice.PeriodStartUtc.ToString("MMMM yyyy")).FontColor(Colors.Grey.Medium);
                    col.Item().Text($"Status: {invoice.Status}").FontColor(StatusColor(invoice.Status));
                    if (invoice.IssuedAt.HasValue)
                        col.Item().Text($"Issued: {invoice.IssuedAt.Value:yyyy-MM-dd}").FontColor(Colors.Grey.Medium);
                });
            });
        };

        private static Action<IContainer> ComposeContent(InvoiceDetailDto invoice) => container =>
        {
            container.PaddingTop(16).Column(col =>
            {
                // ── Period summary tile ───────────────────────────────────────
                col.Item().Border(1).BorderColor("#e5e7eb").Padding(14).Row(row =>
                {
                    SummaryTile(row.RelativeItem(), "Total charged",  $"${invoice.TotalChargedUsd:N4}");
                    SummaryTile(row.RelativeItem(), "Topped up",      $"${invoice.TotalToppedUpUsd:N4}");
                    SummaryTile(row.RelativeItem(), "Meta cost",      $"${invoice.TotalMetaCostUsd:N4}");
                    SummaryTile(row.RelativeItem(), "Margin",         $"${invoice.TotalChargedUsd - invoice.TotalMetaCostUsd:N4}");
                });

                col.Item().PaddingTop(20).Text("Volume").FontSize(11).SemiBold();
                col.Item().PaddingTop(4).Text(text =>
                {
                    text.Span($"{invoice.PaidMessageCount}").SemiBold();
                    text.Span($" paid messages, ");
                    text.Span($"{invoice.FreeMessageCount}").SemiBold();
                    text.Span($" free messages (service window or free-entry-point) during ");
                    text.Span(invoice.PeriodStartUtc.ToString("MMMM yyyy")).SemiBold();
                    text.Span(".");
                });

                if (invoice.ByCategory.Count > 0)
                {
                    col.Item().PaddingTop(20).Text("By category").FontSize(11).SemiBold();
                    col.Item().PaddingTop(6).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Element(HeaderCell).Text("Category");
                            h.Cell().Element(HeaderCell).AlignRight().Text("Messages");
                            h.Cell().Element(HeaderCell).AlignRight().Text("Charged");
                        });
                        foreach (var c in invoice.ByCategory)
                        {
                            table.Cell().Element(BodyCell).Text(c.Category.ToString());
                            table.Cell().Element(BodyCell).AlignRight().Text(c.Count.ToString());
                            table.Cell().Element(BodyCell).AlignRight().Text($"${c.ChargedUsd:N4}");
                        }
                    });
                }

                col.Item().PaddingTop(28).Text("This statement summarises wallet activity for the workspace during the period above. Per-message detail is available in the Billing → Analytics page; raw transactions can be exported as CSV from the same page.").FontSize(9).FontColor(Colors.Grey.Medium);
            });
        };

        private static void SummaryTile(IContainer container, string label, string value)
        {
            container.Column(col =>
            {
                col.Item().Text(label.ToUpperInvariant()).FontSize(8).FontColor(Colors.Grey.Medium).LetterSpacing(0.4f);
                col.Item().PaddingTop(4).Text(value).FontSize(14).Bold();
            });
        }

        private static IContainer HeaderCell(IContainer container) =>
            container.PaddingVertical(6).PaddingHorizontal(8).BorderBottom(1).BorderColor("#e5e7eb")
                     .DefaultTextStyle(t => t.FontSize(8).FontColor(Colors.Grey.Medium).LetterSpacing(0.3f));

        private static IContainer BodyCell(IContainer container) =>
            container.PaddingVertical(6).PaddingHorizontal(8).BorderBottom(1).BorderColor("#f3f4f6");

        private static string StatusColor(InvoiceStatus s) => s switch
        {
            InvoiceStatus.Issued => "#16a34a",
            InvoiceStatus.Void   => "#dc2626",
            _                    => "#6b7280"
        };
    }
}
