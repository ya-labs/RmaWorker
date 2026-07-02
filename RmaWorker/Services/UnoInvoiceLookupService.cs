using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using RmaWorker.Configuration;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;

namespace RmaWorker.Services;

public sealed class UnoInvoiceLookupService : IUnoInvoiceLookupService
{
    private const string InitialIssueDate = "01/01/2022";
    private const string CompanySelectionScript = "copiar(0)";

    private static readonly SemaphoreSlim BrowserLock = new(1, 1);

    private static readonly string[] IssueDateFieldNames =
    [
        "dataIni",
        "dtEmissaoInicial",
        "dtEmissaoIni",
        "dataEmissaoInicial",
        "dataEmissaoIni",
        "dtInicial",
        "dataInicial"
    ];

    private static readonly string[] FinalIssueDateFieldNames =
    [
        "dataFim",
        "dtEmissaoFinal",
        "dtEmissaoFim",
        "dataEmissaoFinal",
        "dataEmissaoFim",
        "dtFinal",
        "dataFinal"
    ];

    private static readonly string[] InvoiceNumberFieldNames =
    [
        "nrNotaFiscal",
        "nroNotaFiscal",
        "numNotaFiscal",
        "numeroNotaFiscal",
        "nrNota",
        "numero",
        "notaFiscal"
    ];

    private readonly UnoInvoiceOptions _options;
    private readonly ILogger<UnoInvoiceLookupService> _logger;

    public UnoInvoiceLookupService(
        IOptions<UnoInvoiceOptions> options,
        ILogger<UnoInvoiceLookupService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<InvoiceLookupResponseDto> FindAsync(
        InvoiceLookupRequestDto request,
        CancellationToken cancellationToken)
    {
        var invoiceNumber = Digits(request.InvoiceNumber);
        if (string.IsNullOrWhiteSpace(invoiceNumber))
        {
            return new InvoiceLookupResponseDto(
                "DADOS_AUSENTES",
                "Informe o numero da nota fiscal.",
                request.InvoiceNumber,
                null,
                null,
                null);
        }

        var configurationError = GetConfigurationError();
        if (configurationError is not null)
        {
            return new InvoiceLookupResponseDto(
                "UNO_CONFIG_INCOMPLETA",
                configurationError,
                invoiceNumber,
                null,
                null,
                null);
        }

        await BrowserLock.WaitAsync(cancellationToken);
        try
        {
            return await FindWithBrowserAsync(invoiceNumber, cancellationToken);
        }
        finally
        {
            BrowserLock.Release();
        }
    }

    private async Task<InvoiceLookupResponseDto> FindWithBrowserAsync(
        string invoiceNumber,
        CancellationToken cancellationToken)
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
            AcceptDownloads = true,
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1366, Height = 900 }
        });
        await context.RouteAsync("**/desktop.do?method=logout**", async route => await route.AbortAsync());

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(_options.TimeoutSeconds * 1000);
        page.SetDefaultNavigationTimeout(_options.TimeoutSeconds * 1000);

        try
        {
            await LoginAsync(page);
            await SelectFirstCompanyAsync(page);
            await NavigateToInvoiceSearchAsync(page);
            await FillSearchFieldsAsync(page, invoiceNumber);
            await ClickSearchAsync(page);
            await EnsureInvoiceWasFoundAsync(page);

            var download = await DownloadFirstInvoiceAsync(page);
            var fileName = string.IsNullOrWhiteSpace(download.FileName)
                ? $"nota-fiscal-{invoiceNumber}.pdf"
                : download.FileName;

            return new InvoiceLookupResponseDto(
                "NF_ENCONTRADA",
                "Nota fiscal encontrada no UNO.",
                invoiceNumber,
                fileName,
                "application/pdf",
                Convert.ToBase64String(download.Bytes));
        }
        catch (InvoiceNotFoundException)
        {
            return new InvoiceLookupResponseDto(
                "NF_NAO_ENCONTRADA",
                "Nota fiscal nao encontrada no UNO.",
                invoiceNumber,
                null,
                null,
                null);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or InvalidOperationException)
        {
            var artifact = await SaveFailureArtifactsAsync(page, "uno-invoice-lookup-failure");
            _logger.LogError(ex, "Falha ao buscar nota fiscal no UNO. Nota: {InvoiceNumber}. Artefato: {Artifact}", invoiceNumber, artifact);
            var suffix = string.IsNullOrWhiteSpace(artifact) ? string.Empty : $" Artefato: {artifact}";
            return new InvoiceLookupResponseDto(
                "UNO_ERRO",
                $"Falha ao buscar nota fiscal no UNO: {ex.Message}{suffix}",
                invoiceNumber,
                null,
                null,
                null);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task LoginAsync(IPage page)
    {
        await page.GotoAsync(AbsoluteUrl("sgw0001.do?method=login"), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await EnsureLoginFormAsync(page);

        await FillByNameAsync(page, "login", _options.Login);
        await FillByNameAsync(page, "senha", _options.Password);
        await SubmitCurrentFormAsync(page, "sgw0001.do?method=validarLogin");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await page.WaitForTimeoutAsync(500);

        var html = await GetStableContentAsync(page);
        if (IsUnoSessionEnded(html))
        {
            throw new InvalidOperationException("Sessao do UNO encerrada apos login.");
        }
    }

    private async Task EnsureLoginFormAsync(IPage page)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            if (FindField(page, "login") is not null && FindField(page, "senha") is not null)
            {
                return;
            }

            await page.Context.ClearCookiesAsync();
            await page.GotoAsync(AbsoluteUrl($"sgw0001.do?method=login&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.WaitForTimeoutAsync(700);
        }

        throw new InvalidOperationException("Formulario de login do UNO nao foi encontrado.");
    }

    private async Task SelectFirstCompanyAsync(IPage page)
    {
        var html = await GetStableContentAsync(page);
        if (!html.Contains(CompanySelectionScript, StringComparison.OrdinalIgnoreCase)
            && !html.Contains("Lista de Empresas", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await page.EvaluateAsync(
            @"script => {
                const match = String(script).match(/copiar\s*\(\s*(\d+)\s*\)/i);
                const companyIndex = match ? Number(match[1]) : 0;
                if (typeof copiar === 'function') {
                    copiar(companyIndex);
                    return;
                }

                const row = Array.from(document.querySelectorAll('[onclick]'))
                    .find(element => String(element.getAttribute('onclick')).includes(script));
                if (row) {
                    row.click();
                }
            }",
            CompanySelectionScript);

        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await page.WaitForTimeoutAsync(700);
    }

    private async Task NavigateToInvoiceSearchAsync(IPage page)
    {
        if (!string.IsNullOrWhiteSpace(_options.SearchPath))
        {
            await page.GotoAsync(AbsoluteUrl(_options.SearchPath), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            return;
        }

        if (!await TryClickTextAsync(page, "Vendas"))
        {
            throw new InvalidOperationException("Menu Vendas nao foi encontrado no UNO. Configure UnoInvoice__SearchPath com o caminho da tela de notas fiscais.");
        }

        await page.WaitForTimeoutAsync(500);

        if (!await TryClickTextAsync(page, "Notas Fiscais")
            && !await TryClickTextAsync(page, "Nota Fiscal"))
        {
            throw new InvalidOperationException("Menu Notas Fiscais nao foi encontrado no UNO. Configure UnoInvoice__SearchPath com o caminho da tela de notas fiscais.");
        }

        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await page.WaitForTimeoutAsync(700);
    }

    private async Task FillSearchFieldsAsync(IPage page, string invoiceNumber)
    {
        if (!await FillFirstAvailableAsync(page, IssueDateFieldNames, InitialIssueDate))
        {
            throw new InvalidOperationException("Campo de data de emissao inicial nao foi encontrado na tela de notas fiscais.");
        }

        await FillFirstAvailableAsync(page, FinalIssueDateFieldNames, DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));

        if (!await FillFirstAvailableAsync(page, InvoiceNumberFieldNames, invoiceNumber))
        {
            throw new InvalidOperationException("Campo de numero da nota fiscal nao foi encontrado na tela de notas fiscais.");
        }

        await CheckAllStatusFiltersAsync(page);
    }

    private async Task ClickSearchAsync(IPage page)
    {
        foreach (var frame in page.Frames)
        {
            var locator = frame.Locator(
                "button[onclick*='buscar'], input[type='submit'][value*='Buscar'], input[type='button'][value*='Buscar'], button:has-text('Buscar'), input[type='submit'][value*='Pesquisar'], input[type='button'][value*='Pesquisar'], button:has-text('Pesquisar')").First;
            if (await locator.CountAsync() == 0)
            {
                continue;
            }

            await locator.ClickAsync();
            await page.WaitForTimeoutAsync(1_500);
            return;
        }

        foreach (var frame in page.Frames)
        {
            var hasBuscarFunction = await frame.EvaluateAsync<bool>("() => typeof buscar === 'function'");
            if (!hasBuscarFunction)
            {
                continue;
            }

            await frame.EvaluateAsync("() => buscar()");
            await page.WaitForTimeoutAsync(1_500);
            return;
        }

        await SubmitFirstFormAsync(page);
    }

    private static async Task EnsureInvoiceWasFoundAsync(IPage page)
    {
        var content = await GetAllFramesContentAsync(page);
        if (content.Contains("Nenhum registro encontrado", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvoiceNotFoundException();
        }
    }

    private async Task<InvoiceDownload> DownloadFirstInvoiceAsync(IPage page)
    {
        var fileUrl = await FindInvoiceDownloadUrlAsync(page);
        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            throw new InvoiceNotFoundException();
        }

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds)
        };

        using var response = await httpClient.GetAsync(fileUrl);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? GetFileNameFromUrl(fileUrl)
            ?? "nota-fiscal.pdf";

        return new InvoiceDownload(fileName.Trim('"'), bytes);
    }

    private static async Task<string?> FindInvoiceDownloadUrlAsync(IPage page)
    {
        string[] selectors =
        [
            "a.linklike[target='_blank']:has(img[src*='ico_lupa'])",
            "a.linklike:has(img[src*='ico_lupa'])",
            "a[target='_blank']:has(img[src*='ico_lupa'])",
            "a[href*='rtagateway']:has(img[src*='ico_lupa'])",
            "a[href*='file']:has(img[src*='ico_lupa'])"
        ];

        foreach (var frame in page.Frames.Where(frame => frame.Name.Contains("ifmLista", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var selector in selectors)
            {
                var locator = frame.Locator(selector).First;
                if (await locator.CountAsync() == 0)
                {
                    continue;
                }

                var href = await locator.GetAttributeAsync("href");
                if (string.IsNullOrWhiteSpace(href))
                {
                    continue;
                }

                return Uri.TryCreate(href, UriKind.Absolute, out var absoluteUri)
                    ? absoluteUri.ToString()
                    : new Uri(new Uri(frame.Url), href).ToString();
            }
        }

        return null;
    }

    private static string? GetFileNameFromUrl(string fileUrl)
    {
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var fileName = Path.GetFileName(uri.LocalPath);
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    private async Task<bool> TryClickTextAsync(IPage page, string text)
    {
        foreach (var frame in page.Frames)
        {
            var locator = frame.GetByText(text, new FrameGetByTextOptions { Exact = false }).First;
            if (await locator.CountAsync() == 0)
            {
                continue;
            }

            await locator.ClickAsync();
            return true;
        }

        return false;
    }

    private static async Task CheckAllStatusFiltersAsync(IPage page)
    {
        foreach (var frame in page.Frames)
        {
            await frame.Locator("input[type='checkbox'][name='situacao']").EvaluateAllAsync<bool>(
                @"elements => {
                    for (const element of elements) {
                        element.checked = true;
                    }

                    return true;
                }");
        }
    }

    private async Task<bool> FillFirstAvailableAsync(IPage page, IReadOnlyCollection<string> names, string value)
    {
        foreach (var name in names)
        {
            if (await FillByNameAsync(page, name, value))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> FillByNameAsync(IPage page, string name, string value)
    {
        var locator = FindEditableField(page, name);
        if (locator is null)
        {
            return false;
        }

        await locator.EvaluateAsync(
            "(element, fieldValue) => { element.value = fieldValue; }",
            value);
        return true;
    }

    private static ILocator? FindEditableField(IPage page, string name)
    {
        foreach (var frame in page.Frames)
        {
            var locator = frame.Locator($"input[name='{name}'], textarea[name='{name}'], select[name='{name}']").First;
            if (locator.CountAsync().GetAwaiter().GetResult() > 0)
            {
                return locator;
            }
        }

        return null;
    }

    private static ILocator? FindField(IPage page, string name)
    {
        foreach (var frame in page.Frames)
        {
            var locator = frame.Locator($"input[name='{name}'], textarea[name='{name}'], select[name='{name}']").First;
            if (locator.CountAsync().GetAwaiter().GetResult() > 0)
            {
                return locator;
            }
        }

        return null;
    }

    private async Task SubmitCurrentFormAsync(IPage page, string action)
    {
        var absoluteAction = AbsoluteUrl(action);
        foreach (var frame in page.Frames)
        {
            if (await frame.Locator("form").CountAsync() == 0)
            {
                continue;
            }

            await frame.EvaluateAsync(
                @"payload => {
                    const form = document.forms[0];
                    form.target = '_self';
                    form.action = payload.action;
                    setTimeout(() => form.submit(), 0);
                }",
                new { action = absoluteAction });
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await page.WaitForTimeoutAsync(500);
            return;
        }

        throw new InvalidOperationException($"Formulario nao encontrado para enviar {action}.");
    }

    private static async Task SubmitFirstFormAsync(IPage page)
    {
        foreach (var frame in page.Frames)
        {
            if (await frame.Locator("form").CountAsync() == 0)
            {
                continue;
            }

            await frame.EvaluateAsync(
                @"() => {
                    const form = document.forms[0];
                    setTimeout(() => form.submit(), 0);
                }");
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await page.WaitForTimeoutAsync(700);
            return;
        }

        throw new InvalidOperationException("Formulario de busca da nota fiscal nao foi encontrado.");
    }

    private static async Task<string> GetStableContentAsync(IPage page)
    {
        Exception? lastException = null;
        for (var attempt = 1; attempt <= 10; attempt++)
        {
            try
            {
                await page.WaitForLoadStateAsync(
                    LoadState.DOMContentLoaded,
                    new PageWaitForLoadStateOptions { Timeout = 2_000 });
                return await page.ContentAsync();
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                lastException = ex;
                await page.WaitForTimeoutAsync(500);
            }
        }

        throw new InvalidOperationException("Nao foi possivel ler o HTML do UNO apos aguardar a navegacao.", lastException);
    }

    private static async Task<string> GetAllFramesContentAsync(IPage page)
    {
        var parts = new List<string>();
        foreach (var frame in page.Frames)
        {
            try
            {
                var content = await frame.ContentAsync();
                parts.Add(
                    $"<!-- FRAME name='{WebUtility.HtmlEncode(frame.Name)}' url='{WebUtility.HtmlEncode(frame.Url)}' -->\n{content}");
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                parts.Add(
                    $"<!-- FRAME name='{WebUtility.HtmlEncode(frame.Name)}' url='{WebUtility.HtmlEncode(frame.Url)}' unavailable='{WebUtility.HtmlEncode(ex.Message)}' -->");
            }
        }

        return string.Join("\n\n", parts);
    }

    private async Task<string> SaveFailureArtifactsAsync(IPage page, string name)
    {
        try
        {
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _options.ArtifactsPath));
            Directory.CreateDirectory(directory);
            var safeName = Regex.Replace(name, @"[^a-zA-Z0-9_.-]", "-");
            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var htmlPath = Path.Combine(directory, $"{timestamp}-{safeName}.html");
            var pngPath = Path.Combine(directory, $"{timestamp}-{safeName}.png");

            await File.WriteAllTextAsync(htmlPath, await GetAllFramesContentAsync(page));
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = pngPath, FullPage = true });
            return htmlPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nao foi possivel salvar artefatos da busca de NF no UNO.");
            return string.Empty;
        }
    }

    private string? GetConfigurationError()
    {
        return string.IsNullOrWhiteSpace(_options.BaseUrl)
            || string.IsNullOrWhiteSpace(_options.Login)
            || string.IsNullOrWhiteSpace(_options.Password)
            ? "Configure UnoInvoice__BaseUrl, UnoInvoice__Login e UnoInvoice__Password para buscar NF no UNO."
            : null;
    }

    private string AbsoluteUrl(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        return new Uri(new Uri(EnsureTrailingSlash(_options.BaseUrl)), path).ToString();
    }

    private static bool IsUnoSessionEnded(string html)
    {
        return html.Contains("Sessao Encerrada", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Sessão Encerrada", StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith('/') ? value : $"{value}/";
    }

    private static string Digits(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value, @"\D", string.Empty);
    }

    private sealed record InvoiceDownload(string FileName, byte[] Bytes);

    private sealed class InvoiceNotFoundException : Exception;
}
