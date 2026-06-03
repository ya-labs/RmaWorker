using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using RmaWorker.Configuration;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;
using UglyToad.PdfPig;

namespace RmaWorker.Services;

public sealed class InvoicePdfService : IInvoicePdfService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly HttpClient _httpClient;
    private readonly InvoiceOptions _options;
    private readonly ILogger<InvoicePdfService> _logger;

    public InvoicePdfService(
        HttpClient httpClient,
        IOptions<InvoiceOptions> options,
        ILogger<InvoicePdfService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<InvoiceDataDto> ExtractAsync(
        string invoiceUrl,
        string productCode,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var pdfBytes = await _httpClient.GetByteArrayAsync(invoiceUrl, timeoutCts.Token);
        var invoiceData = ExtractFromPdfBytes(pdfBytes, productCode);

        _logger.LogInformation(
            "Dados extraidos da nota fiscal. Numero: {Number} | Data: {IssuedAt} | NCM: {Ncm} | Valor unitario: {UnitValue}",
            invoiceData.Number,
            invoiceData.IssuedAt,
            invoiceData.Ncm,
            invoiceData.UnitValue);

        return invoiceData;
    }

    internal static InvoiceDataDto ExtractFromPdfBytes(byte[] pdfBytes, string productCode)
    {
        var text = ExtractText(pdfBytes);
        var itemBlock = FindProductBlock(text, productCode);

        return new InvoiceDataDto(
            Number: ExtractInvoiceNumber(text),
            IssuedAt: ExtractIssueDate(text),
            Ncm: string.IsNullOrWhiteSpace(itemBlock) ? null : ExtractNcm(itemBlock),
            UnitValue: string.IsNullOrWhiteSpace(itemBlock) ? null : ExtractUnitValue(itemBlock));
    }

    private static string ExtractText(byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfDocument.Open(stream);
        var builder = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
        }

        return builder.ToString();
    }

    private static string? FindProductBlock(string text, string productCode)
    {
        var normalizedText = Regex.Replace(text, @"\s+", " ");
        var index = normalizedText.IndexOf(productCode, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var length = Math.Min(2500, normalizedText.Length - index);
        return normalizedText.Substring(index, length);
    }

    private static string? ExtractInvoiceNumber(string text)
    {
        var nroMatch = Regex.Match(text, @"(?i)Nro\D{0,5}(?<number>\d{4,})");
        if (nroMatch.Success)
        {
            return nroMatch.Groups["number"].Value;
        }

        nroMatch = Regex.Match(text, @"(?i)\bN[º°]\.?:?\s*(?<number>\d{4,})");
        if (nroMatch.Success)
        {
            return nroMatch.Groups["number"].Value;
        }

        var match = Regex.Match(
            text,
            @"(?is)(?:NF-e|DANFE|NOTA\s+FISCAL).{0,120}?\bN[º°.]?\s*[:.]?\s*(?<number>\d{1,3}(?:\.\d{3})+|\d{4,})");

        if (!match.Success)
        {
            return null;
        }

        return match.Success
            ? Regex.Replace(match.Groups["number"].Value, @"\D", string.Empty)
            : null;
    }

    private static DateOnly? ExtractIssueDate(string text)
    {
        var match = Regex.Match(
            text,
            @"(?is)DATA\s+DE\s+EMISS[ÃA]O.{0,100}?(?<date>\d{2}/\d{2}/\d{4}|\d{4}-\d{2}-\d{2})");

        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["date"].Value;
        string[] formats = ["dd/MM/yyyy", "yyyy-MM-dd"];

        return DateOnly.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private static string? ExtractNcm(string itemBlock)
    {
        var compactMatch = MatchFiscalProductData(itemBlock);
        if (compactMatch.Success)
        {
            return compactMatch.Groups["ncm"].Value;
        }

        var match = Regex.Match(itemBlock, @"(?<!\d)(?<ncm>\d{8})(?!\d)");
        return match.Success ? match.Groups["ncm"].Value : null;
    }

    private static decimal? ExtractUnitValue(string itemBlock)
    {
        var fiscalDataMatch = MatchFiscalProductData(itemBlock);
        if (fiscalDataMatch.Success)
        {
            return ParseDecimal(fiscalDataMatch.Groups["unit"].Value);
        }

        var ncmMatch = Regex.Match(itemBlock, @"(?<!\d)\d{8}(?!\d)");
        if (!ncmMatch.Success)
        {
            return null;
        }

        var compactAfterNcm = Regex.Replace(itemBlock[ncmMatch.Index..], @"\s+", string.Empty);
        var compactMatch = Regex.Match(
            compactAfterNcm,
            @"^\d{8}\d{4}(?<quantity>\d{1,6},\d{2,4})(?<unit>\d{1,3}(?:\.\d{3})*,\d{2,4})");

        if (compactMatch.Success)
        {
            return ParseDecimal(compactMatch.Groups["unit"].Value);
        }

        var values = Regex.Matches(itemBlock[ncmMatch.Index..], @"(?<!\d)\d{1,3}(?:\.\d{3})*,\d{2,4}(?!\d)")
            .Select(match => ParseDecimal(match.Value))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        return values.Length >= 2 ? values[1] : values.FirstOrDefault();
    }

    private static Match MatchFiscalProductData(string itemBlock)
    {
        var compact = Regex.Replace(itemBlock, @"\s+", string.Empty);

        return Regex.Match(
            compact,
            @"(?<ncm>\d{8})(?<cfop>\d{4})(?<quantity>\d{1,6},\d{2})(?<unit>\d{1,3}(?:\.\d{3})*,\d{2})");
    }

    private static decimal? ParseDecimal(string value)
    {
        return decimal.TryParse(value, NumberStyles.Number, PtBr, out var parsed)
            ? parsed
            : null;
    }
}
