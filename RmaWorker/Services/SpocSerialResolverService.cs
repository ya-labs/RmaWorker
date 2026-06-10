using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using RmaWorker.Configuration;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;

namespace RmaWorker.Services;

public sealed class SpocSerialResolverService : ISpocSerialResolverService
{
    private static readonly Regex IdBlockNextBaseSerialRegex = new(
        @"\b0U\d{4}/[0-9A-F]{6}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex IdBlockNextSerialRegex = new(
        @"\bIDBLOCKNEXT/[A-Z0-9/.-]+/\d{6}\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly SpocOptions _options;
    private readonly ILogger<SpocSerialResolverService> _logger;

    public SpocSerialResolverService(
        IOptions<SpocOptions> options,
        ILogger<SpocSerialResolverService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SpocIdBlockNextResolutionDto?> TryResolveIdBlockNextSerialAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return null;
        }

        if (!IsConfigured())
        {
            throw new InvalidOperationException("Configure Spoc__BaseUrl, Spoc__Login e Spoc__Password para acessar o SPOC.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = _options.BrowserHeadless,
                SlowMo = _options.BrowserSlowMoMs,
                Args = ["--no-sandbox", "--disable-dev-shm-usage"]
            });

            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true,
                ViewportSize = new ViewportSize { Width = 1366, Height = 900 }
            });

            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(_options.TimeoutSeconds * 1000);
            page.SetDefaultNavigationTimeout(_options.TimeoutSeconds * 1000);

            try
            {
                await LoginAsync(page);
                await OpenSerialSearchAsync(page);
                await SearchSerialAsync(page, serial.Trim());

                var firstHtml = await page.ContentAsync();
                var baseSerial = IdBlockNextBaseSerialRegex.Match(firstHtml).Value;
                if (string.IsNullOrWhiteSpace(baseSerial))
                {
                    _logger.LogInformation(
                        "SPOC nao encontrou serial base IDBlock Next para {Serial}.",
                        serial);
                    return null;
                }

                await SearchSerialAsync(page, baseSerial);

                var captionTexts = await page.Locator("div.caption b").AllInnerTextsAsync();
                var resolvedSerial = captionTexts
                    .Select(ExtractIdBlockNextSerial)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

                if (string.IsNullOrWhiteSpace(resolvedSerial))
                {
                    var secondHtml = await page.ContentAsync();
                    resolvedSerial = ExtractIdBlockNextSerial(secondHtml);
                }

                if (!string.IsNullOrWhiteSpace(resolvedSerial))
                {
                    _logger.LogInformation(
                        "SPOC resolveu serial {OriginalSerial} para {ResolvedSerial}.",
                        serial,
                        resolvedSerial);

                    return new SpocIdBlockNextResolutionDto(
                        serial.Trim(),
                        baseSerial.ToUpperInvariant(),
                        resolvedSerial);
                }

                return null;
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Falha ao consultar serial {Serial} no SPOC.", serial);
            throw;
        }
    }

    private async Task LoginAsync(IPage page)
    {
        await page.GotoAsync(
            AbsoluteUrl("Login.aspx"),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await page.Locator("input[name='txtEmail']").FillAsync(_options.Login);
        await page.Locator("input[name='txtSenha']").FillAsync(_options.Password);
        await page.Locator("button[id='logar']").PressAsync("Enter");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    private static async Task OpenSerialSearchAsync(IPage page)
    {
        await page.Locator("a[href='../Main/NSerie.aspx']").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    private static async Task SearchSerialAsync(IPage page, string serial)
    {
        await page.Locator("input[name='ctl00$cph$txtSerie']").FillAsync(serial);
        await page.Locator("input[name='ctl00$cph$btnBuscar']").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    private bool IsConfigured()
    {
        return !string.IsNullOrWhiteSpace(_options.BaseUrl)
            && !string.IsNullOrWhiteSpace(_options.Login)
            && !string.IsNullOrWhiteSpace(_options.Password);
    }

    private string AbsoluteUrl(string path)
    {
        if (Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var configuredUri)
            && configuredUri.AbsolutePath.EndsWith(path, StringComparison.OrdinalIgnoreCase))
        {
            return configuredUri.ToString();
        }

        var baseUri = configuredUri is null
            ? new Uri(EnsureTrailingSlash(_options.BaseUrl))
            : new Uri(configuredUri.GetLeftPart(UriPartial.Authority));

        return new Uri(baseUri, path).ToString();
    }

    private static string ExtractIdBlockNextSerial(string value)
    {
        return IdBlockNextSerialRegex.Match(value).Value.ToUpperInvariant();
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    }
}
