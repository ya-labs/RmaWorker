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
    private const int PartsShipmentWarrantyCategoryCode = 7;
    private const int PartsShipmentOutOfWarrantyCategoryCode = 8;
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

        var isPartsShipmentRequest = string.Equals(request.RequestType, "parts", StringComparison.OrdinalIgnoreCase);
        if (isPartsShipmentRequest && string.IsNullOrWhiteSpace(request.PartToSend))
        {
            return new RmaServiceOrderResponseDto("PECA_AUSENTE", "Informe a peca a ser enviada antes de abrir a O.S no UNO.", []);
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

        var configurationError = GetConfigurationError();
        if (configurationError is not null)
        {
            return new RmaServiceOrderResponseDto(
                "UNO_CONFIG_INCOMPLETA",
                configurationError,
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

    public async Task<UnoCustomerValidationDto> ValidateCustomerAsync(
        string? cnpj,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cnpj) || !_cnpjValidator.IsValid(cnpj))
        {
            return new UnoCustomerValidationDto(
                false,
                null,
                null,
                cnpj,
                "CNPJ_INVALIDO",
                "Informe um CNPJ valido para buscar a revenda no UNO.");
        }

        var configurationError = GetConfigurationError();
        if (configurationError is not null)
        {
            return new UnoCustomerValidationDto(
                false,
                null,
                null,
                cnpj,
                "UNO_CONFIG_INCOMPLETA",
                configurationError);
        }

        await BrowserLock.WaitAsync(cancellationToken);
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
            await context.RouteAsync("**/desktop.do?method=logout**", async route => await route.AbortAsync());

            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(_options.TimeoutSeconds * 1000);
            page.SetDefaultNavigationTimeout(_options.TimeoutSeconds * 1000);

            try
            {
                await LoginAsync(page);
                var customer = await FindCustomerAsync(page, cnpj);
                return customer is null
                    ? new UnoCustomerValidationDto(false, null, null, cnpj, "CLIENTE_NAO_ENCONTRADO", "Nao foi encontrado cliente ativo no UNO para o CNPJ informado.")
                    : new UnoCustomerValidationDto(true, customer.Code, customer.Name, customer.Cnpj, "CLIENTE_ENCONTRADO", null);
            }
            finally
            {
                await context.CloseAsync();
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or InvalidOperationException)
        {
            _logger.LogError(ex, "Falha ao validar cliente no UNO.");
            return new UnoCustomerValidationDto(false, null, null, cnpj, "UNO_ERRO", $"Falha ao validar cliente no UNO: {ex.Message}");
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
        await context.RouteAsync("**/desktop.do?method=logout**", async route => await route.AbortAsync());

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
                        results.Add(await OpenItemServiceOrderAsync(page, customer, request, item));
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
        await EnsureLoginFormAsync(page);

        await FillByNameAsync(page, "login", _options.Login);
        await FillByNameAsync(page, "senha", _options.Password);
        await SubmitCurrentFormAsync(page, "sgw0001.do?method=validarLogin");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var html = await GetStableContentAsync(page);
        EnsureUnoSessionIsActive(html);
        if (!html.Contains("UNO ERP", StringComparison.OrdinalIgnoreCase)
            && !page.Url.Contains("desktop", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Login no UNO nao retornou a tela esperada.");
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

            var html = await GetStableContentAsync(page);
            if (!IsUnoSessionEnded(html))
            {
                break;
            }

            _logger.LogWarning("UNO retornou tela de sessao encerrada antes do formulario de login. Reabrindo tela de login.");
            await page.Context.ClearCookiesAsync();
            await page.GotoAsync(AbsoluteUrl($"sgw0001.do?method=login&_={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.WaitForTimeoutAsync(700);
        }

        throw new InvalidOperationException("Formulario de login do UNO nao foi encontrado.");
    }

    private async Task<CustomerLookup?> FindCustomerAsync(IPage page, string cnpj)
    {
        await page.GotoAsync(AbsoluteUrl("cdq0101.do?method=prepListar"), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await FillByNameAsync(page, "cnpj", Digits(cnpj));
        await SubmitCurrentFormAsync(page, "cdq0101.do?method=listar");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var html = await GetStableContentAsync(page);
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
        RmaServiceOrderRequestDto request,
        RmaServiceOrderItemRequestDto item)
    {
        var cnpj = request.Cnpj!;
        var isPartsShipment = string.Equals(request.RequestType, "parts", StringComparison.OrdinalIgnoreCase);
        var serialValidation = await _serialValidationService.ValidateAsync(item.Serial, CancellationToken.None);
        if (!serialValidation.Exists)
        {
            var missingSerialCategory = isPartsShipment
                ? PartsShipmentOutOfWarrantyCategoryCode
                : (int?)null;
            return BuildResult(item.Serial, cnpj, customer.Name, null, null, item.DefectReported, false, null, missingSerialCategory, false, "SERIAL_NAO_ENCONTRADO", "Serial nao encontrado na consulta atual do UNO.", null);
        }

        var warrantyUntil = serialValidation.InvoiceIssuedAt?.AddYears(1);
        var isUnderWarranty = warrantyUntil.HasValue && warrantyUntil.Value >= DateOnly.FromDateTime(DateTime.Today);
        if (request.MaintenanceInWarranty)
        {
            isUnderWarranty = true;
        }

        var categoryCode = isPartsShipment
            ? (isUnderWarranty ? PartsShipmentWarrantyCategoryCode : PartsShipmentOutOfWarrantyCategoryCode)
            : (isUnderWarranty ? WarrantyCategoryCode : OutOfWarrantyCategoryCode);
        var defect = item.DefectReported!.Trim();
        var observations = BuildObservations(request, item);

        var itemCode = await FindItemAsync(page, customer.Code, serialValidation.Serial);
        if (string.IsNullOrWhiteSpace(itemCode))
        {
            itemCode = await CreateItemAsync(page, customer.Code, serialValidation);
        }

        await page.GotoAsync(AbsoluteUrl("osw0001.do?method=prepTela"), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await FillByNameAsync(page, "codCliente", customer.Code);
        await SubmitCurrentFormAsync(page, "osw0001.do?method=buscarCliente", "barraControladora");

        await FillByNameAsync(page, "ccusto", CostCenterCode.ToString(CultureInfo.InvariantCulture));
        await SubmitCurrentFormAsync(page, "osw0001.do?method=buscarCCusto", "barraControladora");

        await FillByNameAsync(page, "codItem", itemCode);
        var itemDialogMessage = await SubmitCurrentFormAndCaptureDialogAsync(
            page,
            "osw0001.do?method=buscarItem",
            "barraControladora");
        if (!string.IsNullOrWhiteSpace(itemDialogMessage))
        {
            return BuildResult(
                serialValidation.Serial,
                cnpj,
                customer.Name,
                serialValidation.ProductCode,
                serialValidation.ProductDescription,
                defect,
                isUnderWarranty,
                warrantyUntil,
                categoryCode,
                false,
                "UNO_ITEM_REJEITADO",
                itemDialogMessage,
                null);
        }

        await FillByNameAsync(page, "codCategoria", categoryCode.ToString(CultureInfo.InvariantCulture));
        await SelectByNameAsync(page, "codCategoria", categoryCode.ToString(CultureInfo.InvariantCulture));
        await FillByNameAsync(page, "codAtendente", AttendantCode.ToString(CultureInfo.InvariantCulture));
        await FillByNameAsync(page, "defeitoRelatado", defect);
        await FillByNameAsync(page, "observacoes", observations);
        await FillByNameAsync(page, "qtd", "1");

        var today = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        await FillByNameAsync(page, "dtAbertura", today);
        await FillByNameAsync(page, "dtPrevisaoConclusao", today);
        await FillByNameAsync(page, "dtComprometida", today);

        await SelectByNameAsync(page, "codStatus", isPartsShipment ? "15" : "10");
        await SelectByNameAsync(page, "modalidade", "1");
        await SelectByNameAsync(page, "origem", "1");
        await SelectByNameAsync(page, "modoResposta", "4");
        await SelectByNameAsync(page, "motivo", "1");
        await SelectByNameAsync(page, "codStatusDefeito", "1");

        await SubmitCurrentFormAsync(page, "osw0001.do?method=gravarDados", "barraControladora", "defeitoRelatado");
        await SubmitCurrentFormAsync(page, "osw0001.do?method=gravarDados&cmd=gravar", "barraControladora", "defeitoRelatado");

        var (serviceOrderCode, finalHtml) = await WaitForServiceOrderCodeAsync(page);
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
                categoryCode,
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
            categoryCode,
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

        var html = await GetStableContentAsync(page);
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

        var html = await GetStableContentAsync(page);
        var match = CreatedItemRegex.Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException($"UNO nao retornou codItem apos criar o item do serial {serialValidation.Serial}.");
        }

        return match.Groups["code"].Value;
    }

    private static async Task<(string Code, string Html)> WaitForServiceOrderCodeAsync(IPage page)
    {
        var html = string.Empty;
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            html = await GetAllFramesContentAsync(page);
            var serviceOrderCode = ExtractServiceOrderCode(html);
            if (!string.IsNullOrWhiteSpace(serviceOrderCode))
            {
                return (serviceOrderCode, html);
            }

            await page.WaitForTimeoutAsync(500);
        }

        return (string.Empty, html);
    }

    private async Task FillByNameAsync(IPage page, string name, string value)
    {
        var locator = FindEditableField(page, name);
        if (locator is null)
        {
            _logger.LogDebug("Campo {Field} nao encontrado/editavel na tela atual do UNO.", name);
            return;
        }

        await locator.EvaluateAsync(
            "(element, fieldValue) => { element.value = fieldValue; }",
            value);
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
            await locator.EvaluateAsync(
                "(element, fieldValue) => { element.value = fieldValue; }",
                value);
        }
        catch (PlaywrightException)
        {
            await locator.SelectOptionAsync(new[] { value });
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

    private async Task SubmitCurrentFormAsync(
        IPage page,
        string action,
        string target = "_self",
        string? preferredFieldName = null)
    {
        var absoluteAction = AbsoluteUrl(action);
        foreach (var frame in OrderFramesForSubmit(page, preferredFieldName))
        {
            bool hasForm;
            try
            {
                hasForm = await frame.Locator("form").CountAsync() > 0;
            }
            catch (PlaywrightException ex) when (IsNavigationSideEffect(ex))
            {
                await page.WaitForTimeoutAsync(300);
                continue;
            }

            if (!hasForm)
            {
                continue;
            }

            try
            {
                await frame.EvaluateAsync(
                    @"payload => {
                        const form = document.forms[0];
                        form.target = payload.target;
                        form.action = payload.action;
                        setTimeout(() => form.submit(), 0);
                    }",
                    new { action = absoluteAction, target });
            }
            catch (PlaywrightException ex) when (IsNavigationSideEffect(ex))
            {
                _logger.LogDebug(ex, "Contexto do UNO destruido durante submit de {Action}; aguardando navegacao.", action);
            }

            if (target == "_self")
            {
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await page.WaitForTimeoutAsync(300);
            }
            else
            {
                await page.WaitForTimeoutAsync(900);
            }

            return;
        }

        throw new InvalidOperationException($"Formulario nao encontrado para enviar {action}.");
    }

    private async Task<string?> SubmitCurrentFormAndCaptureDialogAsync(
        IPage page,
        string action,
        string target = "_self",
        string? preferredFieldName = null)
    {
        var dialogMessage = string.Empty;
        var dialogHandled = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async void Handler(object? _, IDialog dialog)
        {
            dialogMessage = dialog.Message;
            dialogHandled.TrySetResult(dialogMessage);
            await dialog.AcceptAsync();
        }

        page.Dialog += Handler;
        try
        {
            await SubmitCurrentFormAsync(page, action, target, preferredFieldName);
            var completed = await Task.WhenAny(dialogHandled.Task, Task.Delay(600));
            return completed == dialogHandled.Task
                ? await dialogHandled.Task
                : string.IsNullOrWhiteSpace(dialogMessage) ? null : dialogMessage;
        }
        finally
        {
            page.Dialog -= Handler;
        }
    }

    private static IEnumerable<IFrame> OrderFramesForSubmit(IPage page, string? preferredFieldName)
    {
        if (string.IsNullOrWhiteSpace(preferredFieldName))
        {
            return page.Frames;
        }

        return page.Frames
            .OrderByDescending(frame =>
                FrameContainsField(frame, preferredFieldName));
    }

    private static bool FrameContainsField(IFrame frame, string fieldName)
    {
        try
        {
            return frame.Locator($"input[name='{fieldName}'], textarea[name='{fieldName}'], select[name='{fieldName}']").CountAsync().GetAwaiter().GetResult() > 0;
        }
        catch (PlaywrightException ex) when (IsNavigationSideEffect(ex))
        {
            return false;
        }
    }

    private static bool IsNavigationSideEffect(PlaywrightException ex)
    {
        return ex.Message.Contains("Execution context was destroyed", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Frame was detached", StringComparison.OrdinalIgnoreCase);
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
        int? categoryCodeOverride,
        bool ready,
        string status,
        string? reason,
        string? serviceOrderCode)
    {
        var categoryCode = categoryCodeOverride ?? (isUnderWarranty ? WarrantyCategoryCode : OutOfWarrantyCategoryCode);
        var categoryDescription = categoryCode switch
        {
            PartsShipmentWarrantyCategoryCode => "Garantia - Remessa de pecas",
            PartsShipmentOutOfWarrantyCategoryCode => "Fora de garantia - Remessa de pecas",
            _ => isUnderWarranty ? "Garantia manutencao" : "Fora de garantia manutencao"
        };

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

    private static string BuildObservations(
        RmaServiceOrderRequestDto request,
        RmaServiceOrderItemRequestDto item)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(request.PartToSend))
        {
            parts.Add($"Peca a ser enviada: {request.PartToSend.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(request.UnoObservations))
        {
            parts.Add(request.UnoObservations.Trim());
        }

        if (!string.IsNullOrWhiteSpace(item.UnoObservations))
        {
            parts.Add(item.UnoObservations.Trim());
        }

        return string.Join(Environment.NewLine, parts);
    }

    private string? GetConfigurationError()
    {
        return string.IsNullOrWhiteSpace(_options.BaseUrl)
            || string.IsNullOrWhiteSpace(_options.Login)
            || string.IsNullOrWhiteSpace(_options.Password)
            ? "Configure UnoErp__BaseUrl, UnoErp__Login e UnoErp__Password para acessar o UNO."
            : null;
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
        if (IsUnoSessionEnded(html))
        {
            throw new UnoSessionEndedException();
        }
    }

    private static bool IsUnoSessionEnded(string html)
    {
        return html.Contains("Sessão Encerrada", StringComparison.OrdinalIgnoreCase)
            || html.Contains("Sessao Encerrada", StringComparison.OrdinalIgnoreCase)
            || html.Contains("login foi utilizado em outra estação", StringComparison.OrdinalIgnoreCase)
            || html.Contains("login foi utilizado em outra estacao", StringComparison.OrdinalIgnoreCase);
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
