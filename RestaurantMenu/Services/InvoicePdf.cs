using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RestaurantMenu.Helpers;
using RestaurantMenu.Models;

namespace RestaurantMenu.Services;

/// <summary>
/// An invoice as a PDF (A4), in the invoice's language, from what was copied onto it when it was
/// issued, so it reads the same however often it's downloaded. QuestPDF's Community licence
/// (free under USD 1M revenue) is set at startup in Program.cs.
/// </summary>
public static class InvoicePdf
{
    public static string FileName(Invoice i) => $"{i.Number}.pdf";

    public static byte[] Render(Invoice inv)
    {
        var w = BillingText.For(inv.Language);
        var lang = inv.Language;
        string Money(int cents) => PricingRules.Money(cents, inv.Currency, alwaysCents: true);
        var vatLabel = string.Format(w.PdfVat, inv.VatPercent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(44);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor("#161616"));

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("My Quick Menu").FontSize(18).SemiBold();
                    c.Item().Text(inv.SellerName).FontColor("#4A4A47");
                });
                row.ConstantItem(200).AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text($"{w.PdfInvoice} {inv.Number}").FontSize(16).SemiBold();
                    c.Item().AlignRight().Text($"{w.PdfDate}: {BillingText.Date(inv.IssuedUtc, lang)}");
                    c.Item().AlignRight().Text($"{w.PdfDue}: {BillingText.Date(inv.DueUtc, lang)}");
                    if (inv.Status == InvoiceStatus.Paid && inv.PaidUtc is { } paid)
                        c.Item().AlignRight().Text(string.Format(w.PdfPaid, BillingText.Date(paid, lang))).FontColor("#1E7A46").SemiBold();
                });
            });

            page.Content().PaddingTop(28).Column(col =>
            {
                col.Spacing(18);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(w.PdfFrom).FontColor("#6B6B67").FontSize(9);
                        c.Item().Text(inv.SellerName).SemiBold();
                        if (inv.SellerNipt != null) c.Item().Text($"NIPT {inv.SellerNipt}");
                        if (inv.SellerAddress != null) c.Item().Text(inv.SellerAddress);
                    });
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(w.PdfTo).FontColor("#6B6B67").FontSize(9);
                        c.Item().Text(inv.BuyerName).SemiBold();
                        if (inv.BuyerNipt != null) c.Item().Text($"NIPT {inv.BuyerNipt}");
                        if (inv.BuyerAddress != null) c.Item().Text(inv.BuyerAddress);
                        c.Item().Text(inv.BuyerEmail);
                    });
                });

                col.Item().Text($"{w.PdfPeriod}: {BillingText.Date(inv.PeriodStartUtc, lang)} – {BillingText.LastDay(inv.PeriodEndUtc, lang)}");

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(5);
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                        c.RelativeColumn(2);
                    });
                    static IContainer Head(IContainer c) => c.BorderBottom(1).BorderColor("#D9D9D4").PaddingVertical(6);
                    static IContainer Cell(IContainer c) => c.BorderBottom(1).BorderColor("#EEEEEA").PaddingVertical(6);
                    t.Header(h =>
                    {
                        h.Cell().Element(Head).Text(w.PdfItem).SemiBold();
                        h.Cell().Element(Head).AlignRight().Text(w.PdfQty).SemiBold();
                        h.Cell().Element(Head).AlignRight().Text(w.PdfUnit).SemiBold();
                        h.Cell().Element(Head).AlignRight().Text(w.PdfAmount).SemiBold();
                    });
                    foreach (var l in inv.Lines.OrderBy(l => l.Module))
                    {
                        var perBranch = EntitlementRules.IsPerBranch(l.Module) ? $" ({w.PdfPerBranch})" : "";
                        t.Cell().Element(Cell).Text(BillingText.Module(lang, l.Module) + perBranch);
                        t.Cell().Element(Cell).AlignRight().Text(l.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        t.Cell().Element(Cell).AlignRight().Text(Money(l.UnitCents));
                        t.Cell().Element(Cell).AlignRight().Text(Money(l.TotalCents));
                    }
                });

                col.Item().AlignRight().Width(240).Column(c =>
                {
                    c.Item().Row(r => { r.RelativeItem().Text(w.PdfSubtotal); r.RelativeItem().AlignRight().Text(Money(inv.SubtotalCents)); });
                    if (inv.VatCents > 0)
                        c.Item().Row(r => { r.RelativeItem().Text(vatLabel); r.RelativeItem().AlignRight().Text(Money(inv.VatCents)); });
                    c.Item().PaddingTop(4).BorderTop(1).BorderColor("#161616").PaddingTop(4)
                        .Row(r => { r.RelativeItem().Text(w.PdfTotal).SemiBold(); r.RelativeItem().AlignRight().Text(Money(inv.TotalCents)).SemiBold().FontSize(12); });
                    if (inv.VatCents == 0) c.Item().PaddingTop(4).Text(w.PdfNoVat).FontColor("#6B6B67").FontSize(9);
                });

                if (inv.Provider == BillingProvider.BankTransfer && inv.SellerIban != null)
                {
                    col.Item().Background("#F4F4F2").Padding(12).Column(c =>
                    {
                        c.Spacing(2);
                        c.Item().Text(w.PdfPayTo).SemiBold();
                        c.Item().Text($"{w.Beneficiary}: {inv.SellerName}");
                        if (inv.SellerBank != null) c.Item().Text($"{w.Bank}: {inv.SellerBank}");
                        c.Item().Text($"{w.Iban}: {InvoiceRules.FormatIban(inv.SellerIban)}");
                        if (inv.SellerSwift != null) c.Item().Text($"{w.Swift}: {inv.SellerSwift}");
                        c.Item().Text($"{w.Reference}: {inv.Number}").SemiBold();
                    });
                }
                if (inv.FiscalCode != null) col.Item().Text($"NIVF/NSLF: {inv.FiscalCode}").FontSize(9).FontColor("#6B6B67");
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.Span("myquickmenu.com · ").FontColor("#6B6B67").FontSize(8);
                t.CurrentPageNumber().FontSize(8).FontColor("#6B6B67");
            });
        })).GeneratePdf();
    }
}
