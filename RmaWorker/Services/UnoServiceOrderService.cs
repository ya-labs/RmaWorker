using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using RmaWorker.Configuration;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;

namespace RmaWorker.Services;

public sealed class UnoServiceOrderService : IUnoServiceOrderService
{
    private const int CostCenterCode = 14;
    private const int WarrantyCategoryCode = 2;
    private const int OutOfWarrantyCategoryCode = 5;
    private const int AttendantCode = 906;
    private const int Quantity = 1;

    private static readonly SemaphoreSlim BrowserLock = new(1, 1);

    private static readonly Regex CustomerRowRegex = new(
        @"<td[^>]*>\s*&nbsp;(?<code>\d+)</td>\s*<td[^>]*>\s*&nbsp;(?<name>.*?)</td>\s*<td[^>]*>\s*&nbsp;(?<cnpj>[\d./-]+)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CustomerCopyRegex = new(
        @"copiar\s*\(\s*['""]?(?<code>\d{2,})['""]?\s*,",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExistingItemRegex = new(
        @"(?:carregarItem|copiar)\s*\(\s*'?(?<code>\d+)'?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CreatedItemRegex = new(
        "name=\"codItem\"[^>]*value=\"(?<code>\\d+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ISerialValidationService _serialValidationService;
    private readonly ICnpjValidator _cnpjValidator;
    private readonly UnoErpOptions _options;
    private readonly ILogger<UnoServiceOrderService> _logger;

    public UnoServiceOrderService(
        ISerialValidationService serialValidationService,
        ICnpjValidator cnpjValidator,
        IOptions<UnoErpOptions> options,
        ILogger<UnoServiceOrderService> logger)
    {
        _serialValidationService = serialValidationService;
        _cnpjValidator = cnpjValidator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RmaServiceOrderResponseDto> OpenAsync(
        RmaServiceOrderRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            return new RmaServiceOrderResponseDto("DADOS_AUSENTES", "Informe pelo menos um numero de serie para abrir a O.S.", []);
        }

        if (string.IsNullOrWhiteSpace(request.Cnpj) || !_cnpjValidator.IsValid(request.Cnpj))
        {
            return new RmaServiceOrderResponseDto("CNPJ_INVALIDO", "Informe um CNPJ valido para buscar a revenda no UNO.", []);
        }

        var missingDefectItems = request.Items
            .Where(item => string.IsNullOrWhiteSpace(item.DefectReported))
            .ToList();
        if (missingDefectItems.Count > 0)
        {
            var missingResults = missingDefectItems
                .Select(item => BuildResult(
                    item.Serial,
                    request.Cnpj,
                    null,
                    null,
                    null,
                    item.DefectReported,
                    false,
                    null,
                    false,
                    "DEFEITO_AUSENTE",
                    "Informe o defeito relatado antes de abrir a O.S no UNO.",
                    null))
                .ToList();

            return new RmaServiceOrderResponseDto(
                "DEFEITO_AUSENTE",
                "Informe o defeito relatado antes de abrir a O.S no UNO.",
                missingResults);
        }

        if (string.IsNullOrWhiteSpace(_options.Login) || string.IsNullOrWhiteSpace(_options.Password))
        {
            return new RmaServiceOrderResponseDto(
                "UNO_CONFIG_INCOMPLETA",
                "Configure UnoErp__Login e UnoErp__Password para abrir a O.S no UNO.",
                []);
        }

        await BrowserLock.WaitAsync(cancellationToken);
        try
        {
            return await OpenWithBrowserAsync(request, cancellationToken);
        }
        finally
        {
            BrowserLock.Release();
        }
    }

    private async Task<RmaServiceOrderResponseDto> OpenWithBrowserAsync(
        RmaServiceOrderRequestDto request,
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
            IgnoreHTTPSErrors = true,
            ViewportSize = new ViewportSize { Width = 1366, Height = 900 }
        });

        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(_options.TimeoutSeconds * 1000);
        page.SetDefaultNavigationTimeout(_options.TimeoutSeconds * 1000);

        try
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    await LoginAsync(page);

                    var customer = await FindCustomerAsync(page, request.Cnpj!);
                    if (customer is null)
                    {
                        return new RmaServiceOrderResponseDto(
                            "CLIENTE_NAO_ENCONTRADO",
                            "Nao foi encontrado cliente ativo no UNO para o CNPJ informado.",
                            []);
                    }

                    var results = new List<RmaServiceOrderItemResultDto>();
                    foreach (var item in request.Items)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        results.Add(await OpenItemServiceOrderAsync(page, customer, request.Cnpj!, item));
                    }

                    var failed = results.Where(result => result.Status != "OS_ABERTA").ToList();
                    return failed.Count == 0
                        ? new RmaServiceOrderResponseDto("OS_ABERTA", "O.S aberta no UNO.", results)
                        : new RmaServiceOrderResponseDto("OS_PARCIAL", "Uma ou mais O.S nao foram abertas no UNO.", results);
                }
                catch (UnoSessionEndedException) when (attempt == 1)
                {
                    _logger.LogWarning("Sessao do UNO encerrada apos login. Tentando relogar uma vez.");
                    await page.Context.ClearCookiesAsync();
                    await page.WaitForTimeoutAsync(1500);
                }
            }

            throw new UnoSessionEndedException();
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or InvalidOperationException or UnoSessionEndedException)
        {
            var artifact = await SaveFailureArtifactsAsync(page, "uno-open-os-failure");
            _logger.LogError(ex, "Falha ao abrir O.S no UNO via navegador. Artefato: {Artifact}", artifact);
            var suffix = string.IsNullOrWhiteSpace(artifact) ? string.Empty : $" Artefato: {artifact}";
            return new RmaServiceOrderResponseDto("UNO_ERRO", $"Falha ao abrir O.S no UNO: {ex.Message}{suffix}", []);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task LoginAsync(IPage page)
    {
        await page.GotoAsync(AbsoluteUrl("sgw0001.do?method=login"), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        EnsureUnoSessionIsActive(await page.ContentAsync());

        await FillByNameAsync(page, "login", _options.Login);
        await FillByNameAsync(page, "senha", _options.Password);
        await SubmitCurrentFormAsync(page, "sgw0001.do?method=validarLogin");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var html = await page.ContentAsync();
        EnsureUnoSessionIsActive(html);
        if (!html.Contains("UNO ERP", StringComparison.OrdinalIgnoreCase)
            && !page.Url.Contains("desktop", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Login no UNO nao retornou a tela esperada.");
        }
    }

    private async Task<CustomerLookup?> FindCustomerAsync(IPage page, string cnpj)
    {
        await page.GotoAsync(AbsoluteUrl("cdq0101.do?method=prepListar"), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await FillByNameAsync(page, "cnpj", Digits(cnpj));
        await SubmitCurrentFormAsync(page, "cdq0101.do?method=listar");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var html = await page.ContentAsync();
        EnsureUnoSessionIsActive(html);
        var match = CustomerRowRegex.Match(html);
        if (match.Success)
        {
            return new CustomerLookup(
                WebUtility.HtmlDecode(match.Groups["code"].Value.Trim()),
                CleanHtml(match.Groups["name"].Value),
                WebUtility.HtmlDecode(match.Groups["cnpj"].Value.Trim()));
        }

        var normalizedCnpj = Digits(cnpj);
        var pageContainsCnpj = Digits(html).Contains(normalizedCnpj, StringComparison.Ordinal);
        var copyMatch = CustomerCopyRegex.Match(html);
        if (pageContainsCnpj && copyMatch.Success)
        {
            return new CustomerLookup(
                copyMatch.Groups["code"].Value.Trim(),
                "Cliente UNO",
                normalizedCnpj);
        }

        var artifact = await SaveFailureArtifactsAsync(page, "uno-customer-not-found");
        _logger.LogWarning(
            "Cliente nao reconhecido na busca do UNO. CNPJ: {Cnpj}. Url: {Url}. Artefato: {Artifact}",
            normalizedCnpj,
            page.Url,
            artifact);

        return null;
    }

    private async Task<RmaServiceOrderItemResultDto> OpenItemServiceOrderAsync(
        IPage page,
        CustomerLookup customer,
        string cnpj,
        RmaServiceOrderItemRequestDto item)
    {
        var serialValidation = await _serialValidationService.ValidateAsync(item.Serial, CancellationToken.None);
        if (!serialValidation.Exists)
        {
            return BuildResult(item.Serial, cnpj, customer.Name, null, null, item.DefectReported, false, null, false, "SERIAL_NAO_ENCONTRADO", "Serial nao encontrado na consulta atual do UNO.", null);
        }

        var warrantyUntil = serialValidation.InvoiceIssuedAt?.AddYears(1);
        var isUnderWarranty = warrantyUntil.HasValue && warrantyUntil.Value >= DateOnly.FromDateTime(DateTime.Today);
        var categoryCode = isUnderWarranty ? WarrantyCategoryCode : OutOfWarrantyCategoryCode;
        var defect = item.DefectReported!.Trim();

        var itemCode = await FindItemAsync(page, customer.Code, serialValidation.Serial);
        if (string.IsNullOrWhiteSpace(itemCode))
        {
            itemCode = await CreateItemAsync(page, customer.Code, serialValidation);
        }

        await page.GotoAsync(AbsoluteUrl("osw0001.do?method=prepTela"), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await FillByNameAsync(page, "codCliente", customer.Code);
        await SubmitCurrentFormAsync(page, "osw0001.do?method=buscarCliente");

        await FillByNameAsync(page, "ccusto", CostCenterCode.ToString(CultureInfo.InvariantCulture));
        await SubmitCurrentFormAsync(page, "osw0001.do?method=buscarCCusto");

        await FillByNameAsync(page, "codItem", itemCode);
        await SubmitCurrentFormAsync(page, "osw0001.do?method=buscarItem");

        await FillByNameAsync(page, "codCategoria", categoryCode.ToString(CultureInfo.InvariantCulture));
        await SelectByNameAsync(page, "codCategoria", categoryCode.ToString(CultureInfo.InvariantCulture));
        await FillByNameAsync(page, "codAtendente", AttendantCode.ToString(CultureInfo.InvariantCulture));
        await FillByNameAsync(page, "defeitoRelatado", defect);
        await FillByNameAsync(page, "qtd", "1");

        var today = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        await FillByNameAsync(page, "dtAbertura", today);
        await FillByNameAsync(page, "dtPrevisaoConclusao", today);
        await FillByNameAsync(page, "dtComprometida", today);

        await SelectByNameAsync(page, "codStatus", "10");
        await SelectByNameAsync(page, "modalidade", "1");
        await SelectByNameAsync(page, "origem", "1");
        await SelectByNameAsync(page, "modoResposta", "4");
        await SelectByNameAsync(page, "motivo", "1");
        await SelectByNameAsync(page, "codStatusDefeito", "1");

        await SubmitCurrentFormAsync(page, "osw0001.do?method=gravarDados");
        await SubmitCurrentFormAsync(page, "osw0001.do?method=gravarDados&cmd=gravar");
        await SubmitCurrentFormAsync(page, "osw0001.do?method=gravar");

        var finalHtml = await page.ContentAsync();
        var serviceOrderCode = ExtractServiceOrderCode(finalHtml);
        if (string.IsNullOrWhiteSpace(serviceOrderCode))
        {
            var artifact = await SaveFailureArtifactsAsync(page, $"uno-os-not-confirmed-{serialValidation.Serial.Replace('/', '-')}");
            var message = ExtractControllerMessage(finalHtml);
            return BuildResult(
                serialValidation.Serial,
                cnpj,
                customer.Name,
                serialValidation.ProductCode,
                serialValidation.ProductDescription,
                defect,
                isUnderWarranty,
                warrantyUntil,
                false,
                "UNO_OS_NAO_CONFIRMADA",
                string.IsNullOrWhiteSpace(message)
                    ? $"UNO nao retornou codigo de O.S apos gravar. Artefato: {artifact}"
                    : $"{message} Artefato: {artifact}",
                null);
        }

        return BuildResult(
            serialValidation.Serial,
            cnpj,
            customer.Name,
            serialValidation.ProductCode,
            serialValidation.ProductDescription,
            defect,
            isUnderWarranty,
            warrantyUntil,
            true,
            "OS_ABERTA",
            null,
            serviceOrderCode);
    }

    private async Task<string?> FindItemAsync(IPage page, string customerCode, string serial)
    {
        await page.GotoAsync(
            AbsoluteUrl($"eqq0008.do?method=prepListar&codCliente={Uri.EscapeDataString(customerCode)}&fixaCliente=true&codPlano="),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await FillByNameAsync(page, "nrSerie", serial);
        await SubmitCurrentFormAsync(page, "eqq0008.do?method=listar");

        var html = await page.ContentAsync();
        var match = ExistingItemRegex.Match(html);
        return match.Success ? match.Groups["code"].Value : null;
    }

    private async Task<string> CreateItemAsync(
        IPage page,
        string customerCode,
        SerialValidationResultDto serialValidation)
    {
        if (string.IsNullOrWhiteSpace(serialValidation.ProductCode))
        {
            throw new InvalidOperationException($"Consulta do serial {serialValidation.Serial} nao retornou codigo do produto.");
        }

        await page.GotoAsync(
            AbsoluteUrl($"eqw0017.do?method=prepTela&codCliente={Uri.EscapeDataString(customerCode)}&fixaCliente=true"),
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await FillByNameAsync(page, "codCliente", customerCode);
        await FillByNameAsync(page, "codProduto", serialValidation.ProductCode);
        await FillByNameAsync(page, "descComercial", serialValidation.ProductDescription ?? string.Empty);
        await FillByNameAsync(page, "qtd", "1");
        await FillByNameAsync(page, "nrSerie", serialValidation.Serial);
        await SelectByNameAsync(page, "situacao", "1");
        await SubmitCurrentFormAsync(page, "eqw0017.do?method=gravar");

        var html = await page.ContentAsync();
        var match = CreatedItemRegex.Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException($"UNO nao retornou codItem apos criar o item do serial {serialValidation.Serial}.");
        }

        return match.Groups["code"].Value;
    }

    private async Task FillByNameAsync(IPage page, string name, string value)
    {
        var locator = FindEditableField(page, name);
        if (locator is null)
        {
            _logger.LogDebug("Campo {Field} nao encontrado/editavel na tela atual do UNO.", name);
            return;
        }

        await locator.FillAsync(value);
    }

    private static async Task SelectByNameAsync(IPage page, string name, string value)
    {
        var locator = FindField(page, name);
        if (locator is null)
        {
            return;
        }

        try
        {
            await locator.SelectOptionAsync(new[] { value });
        }
        catch (PlaywrightException)
        {
            await locator.FillAsync(value);
        }
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
            var hasForm = await frame.Locator("form").CountAsync() > 0;
            if (!hasForm)
            {
                continue;
            }

            await frame.EvaluateAsync(
                @"([action]) => {
                    const form = document.forms[0];
                    form.target = '_self';
                    form.action = action;
                    form.submit();
                }",
                new[] { absoluteAction });
            await page.WaitForTimeoutAsync(900);
            return;
        }

        throw new InvalidOperationException($"Formulario nao encontrado para enviar {action}.");
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

            await File.WriteAllTextAsync(htmlPath, await page.ContentAsync());
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = pngPath, FullPage = true });
            return htmlPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nao foi possivel salvar artefatos do UNO.");
            return string.Empty;
        }
    }

    private static RmaServiceOrderItemResultDto BuildResult(
        string serial,
        string cnpj,
        string? customerName,
        string? productCode,
        string? productDescription,
        string? defect,
        bool isUnderWarranty,
        DateOnly? warrantyUntil,
        bool ready,
        string status,
        string? reason,
        string? serviceOrderCode)
    {
        var categoryCode = isUnderWarranty ? WarrantyCategoryCode : OutOfWarrantyCategoryCode;
        var categoryDescription = isUnderWarranty ? "Garantia manutencao" : "Fora de garantia manutencao";

        return new RmaServiceOrderItemResultDto(
            serial,
            cnpj,
            customerName,
            productCode,
            productDescription,
            defect,
            CostCenterCode,
            categoryCode,
            categoryDescription,
            AttendantCode,
            Quantity,
            isUnderWarranty,
            warrantyUntil,
            ready,
            status,
            reason,
            serviceOrderCode);
    }

    private string AbsoluteUrl(string path)
    {
        return new Uri(new Uri(EnsureTrailingSlash(_options.BaseUrl)), path).ToString();
    }

    private static string ExtractInputValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $"name=\"{Regex.Escape(name)}\"(?=[\\s>])[^>]*value=\"([^\"]*)\"",
            RegexOptions.IgnoreCase);

        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
    }

    private static string ExtractServiceOrderCode(string html)
    {
        var value = ExtractInputValue(html, "codOs");
        return IsValidServiceOrderCode(value) ? value : string.Empty;
    }

    private static bool IsValidServiceOrderCode(string value)
    {
        var digits = Digits(value);
        return digits.Length >= 5 && digits != "0001";
    }

    private static string ExtractControllerMessage(string html)
    {
        var match = Regex.Match(
            html,
            "id=\"divMensagens\"[^>]*>(.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success ? CleanHtml(match.Groups[1].Value) : string.Empty;
    }

    private static void EnsureUnoSessionIsActive(string html)
    {
        if (html.Contains("Sessão Encerrada", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Sessao Encerrada", StringComparison.OrdinalIgnoreCase)
            || html.Contains("login foi utilizado em outra estação", StringComparison.OrdinalIgnoreCase)
            || html.Contains("login foi utilizado em outra estacao", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnoSessionEndedException();
        }
    }

    private static string CleanHtml(string value)
    {
        var withoutTags = Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline);
        return WebUtility.HtmlDecode(withoutTags).Replace("&nbsp;", " ").Trim();
    }

    private static string Digits(string value)
    {
        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    }

    private sealed record CustomerLookup(string Code, string Name, string Cnpj);

    private sealed class UnoSessionEndedException : InvalidOperationException
    {
        public UnoSessionEndedException()
            : base("Sessao do UNO encerrada porque o usuario configurado foi utilizado em outra estacao. Feche outros acessos do usuario UNO ou configure um usuario dedicado para a automacao.")
        {
        }
    }
}
