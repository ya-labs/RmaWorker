using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using RmaWorker.Configuration;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;

namespace RmaWorker.Services;

public sealed class UnoOccurrenceService : IUnoOccurrenceService
{
    private const string DefaultCustomerCode = "2";
    private const string DefaultCostCenterCode = "14";
    private const string DefaultStatusCode = "50";
    private const int MaxConcurrentBrowsers = 3;

    private static readonly SemaphoreSlim BrowserConcurrencyLock = new(MaxConcurrentBrowsers, MaxConcurrentBrowsers);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BrowserLocksByLogin = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex CustomerRowRegex = new(
        @"<td[^>]*>\s*&nbsp;(?<code>\d+)</td>\s*<td[^>]*>\s*&nbsp;(?<name>.*?)</td>\s*<td[^>]*>\s*&nbsp;(?<cnpj>[\d./-]+)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CustomerCopyRegex = new(
        @"copiar\s*\(\s*['""]?(?<code>\d{2,})['""]?\s*,",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly ICnpjValidator _cnpjValidator;
    private readonly UnoErpOptions _options;
    private readonly ILogger<UnoOccurrenceService> _logger;

    public UnoOccurrenceService(
        ICnpjValidator cnpjValidator,
        IOptions<UnoErpOptions> options,
        ILogger<UnoOccurrenceService> logger)
    {
        _cnpjValidator = cnpjValidator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OccurrenceOpenResponseDto> OpenAsync(
        OccurrenceOpenRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Failed("TITULO_AUSENTE", "Informe o titulo da ocorrência antes de finalizar.");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Failed("DESCRICAO_AUSENTE", "Informe a descrição da ocorrência antes de finalizar.");
        }

        if (!TryParsePositiveCode(request.CategoryCode, out var categoryCode))
        {
            return Failed("CATEGORIA_AUSENTE", "Informe o código da categoria/equipamento da ocorrência.");
        }

        if (!TryParsePositiveCode(request.OccurrenceTypeCode, out var occurrenceTypeCode, "1"))
        {
            return Failed("TIPO_OCORRENCIA_INVALIDO", "Informe um tipo de ocorrencia valido.");
        }

        if (!TryParsePositiveCode(request.StatusCode, out var statusCode, DefaultStatusCode))
        {
            return Failed("STATUS_OCORRENCIA_INVALIDO", "Informe um status de ocorrencia valido.");
        }

        if (!TryParsePositiveCode(request.CostCenterCode, out var costCenterCode, DefaultCostCenterCode))
        {
            return Failed("CENTRO_CUSTO_INVALIDO", "Informe um centro de custo valido.");
        }

        var credentials = ResolveCredentials(request.UnoLogin, request.UnoPassword);
        var configurationError = GetConfigurationError(credentials);
        if (configurationError is not null)
        {
            return Failed("UNO_CONFIG_INCOMPLETA", configurationError);
        }

        var loginLock = GetBrowserLock(credentials!.Login);
        await loginLock.WaitAsync(cancellationToken);
        await BrowserConcurrencyLock.WaitAsync(cancellationToken);
        try
        {
            return await OpenWithBrowserAsync(request, credentials, categoryCode, occurrenceTypeCode, statusCode, costCenterCode, cancellationToken);
        }
        finally
        {
            BrowserConcurrencyLock.Release();
            loginLock.Release();
        }
    }

    private async Task<OccurrenceOpenResponseDto> OpenWithBrowserAsync(
        OccurrenceOpenRequestDto request,
        UnoCredentials credentials,
        int categoryCode,
        int occurrenceTypeCode,
        int statusCode,
        int costCenterCode,
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
                    await LoginAsync(page, credentials);
                    var customer = await ResolveCustomerAsync(page, request.Cnpj);
                    var occurrenceCode = await OpenOccurrenceAsync(page, request, customer, categoryCode, occurrenceTypeCode, statusCode, costCenterCode);

                    return new OccurrenceOpenResponseDto(
                        "OC_ABERTA",
                        string.IsNullOrWhiteSpace(occurrenceCode)
                            ? "Ocorrência enviada ao UNO, mas o código não foi identificado automaticamente."
                            : $"Ocorrência {occurrenceCode} aberta no UNO.",
                        occurrenceCode,
                        customer.Code,
                        customer.Name,
                        categoryCode.ToString(CultureInfo.InvariantCulture),
                        request.Title.Trim());
                }
                catch (UnoSessionEndedException) when (attempt == 1)
                {
                    _logger.LogWarning("Sessao do UNO encerrada ao abrir O.C. Tentando relogar uma vez.");
                    await page.Context.ClearCookiesAsync();
                    await page.WaitForTimeoutAsync(1500);
                }
            }

            throw new UnoSessionEndedException();
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or InvalidOperationException or UnoSessionEndedException)
        {
            var artifact = await SaveFailureArtifactsAsync(page, "uno-open-oc-failure");
            _logger.LogError(ex, "Falha ao abrir O.C no UNO via navegador. Artefato: {Artifact}", artifact);
            var suffix = string.IsNullOrWhiteSpace(artifact) ? string.Empty : $" Artefato: {artifact}";
            return Failed("UNO_ERRO", $"Falha ao abrir O.C no UNO: {ex.Message}{suffix}");
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    private async Task<CustomerLookup> ResolveCustomerAsync(IPage page, string? cnpj)
    {
        if (!string.IsNullOrWhiteSpace(cnpj) && _cnpjValidator.IsValid(cnpj))
        {
            var customer = await FindCustomerAsync(page, cnpj);
            if (customer is not null)
            {
                return customer;
            }

            _logger.LogWarning("Cliente nao encontrado para CNPJ {Cnpj}. O.C sera aberta no cliente padrao {DefaultCustomer}.", Digits(cnpj), DefaultCustomerCode);
        }

        return new CustomerLookup(DefaultCustomerCode, "Cliente padrão UNO", null);
    }

    private async Task<string> OpenOccurrenceAsync(
        IPage page,
        OccurrenceOpenRequestDto request,
        CustomerLookup customer,
        int categoryCode,
        int occurrenceTypeCode,
        int statusCode,
        int costCenterCode)
    {
        await page.GotoAsync(AbsoluteUrl("ocw0001.do?method=prepTela"), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await FillByNameAsync(page, "codCliente", customer.Code, dispatchChange: false);
        await SubmitCurrentFormAsync(page, "ocw0001.do?method=buscarCliente", preferredFieldName: "codCliente");

        await FillByNameAsync(page, "codCategoria", categoryCode.ToString(CultureInfo.InvariantCulture), dispatchChange: false);
        await SubmitCurrentFormAsync(page, "ocw0001.do?method=buscarCategoria", preferredFieldName: "codCategoria");

        await FillOccurrenceFieldsAsync(page, request, customer.Code, categoryCode, occurrenceTypeCode, statusCode, costCenterCode);

        using var dialogCapture = new UnoDialogCapture(page);
        await SubmitCurrentFormAsync(page, "ocw0001.do?method=gravar", "_self", "descricao");
        var dialogMessage = await dialogCapture.WaitForMessageAsync(2_000);
        if (!string.IsNullOrWhiteSpace(dialogMessage))
        {
            throw new InvalidOperationException(dialogMessage);
        }

        var (occurrenceCode, html) = await WaitForOccurrenceCodeAsync(page);
        var controllerMessage = ExtractControllerMessage(html);
        if (string.IsNullOrWhiteSpace(occurrenceCode) && !string.IsNullOrWhiteSpace(controllerMessage))
        {
            _logger.LogWarning("UNO retornou mensagem apos gravar O.C: {Message}", controllerMessage);
        }

        return occurrenceCode;
    }

    private async Task FillOccurrenceFieldsAsync(
        IPage page,
        OccurrenceOpenRequestDto request,
        string customerCode,
        int categoryCode,
        int occurrenceTypeCode,
        int statusCode,
        int costCenterCode)
    {
        await FillByNameAsync(page, "codCliente", customerCode, dispatchChange: false);
        await FillByNameAsync(page, "codCategoria", categoryCode.ToString(CultureInfo.InvariantCulture), dispatchChange: false);
        await FillByNameAsync(page, "descAbrev", request.Title.Trim());
        await FillByNameAsync(page, "descricao", request.Description.Trim());
        await SelectByNameAsync(page, "tpOcorrencia", occurrenceTypeCode.ToString(CultureInfo.InvariantCulture));
        await SelectByNameAsync(page, "centroCusto", costCenterCode.ToString(CultureInfo.InvariantCulture));
        await SelectByNameAsync(page, "codStatus", statusCode.ToString(CultureInfo.InvariantCulture));
    }

    private async Task LoginAsync(IPage page, UnoCredentials credentials)
    {
        await page.GotoAsync(AbsoluteUrl("sgw0001.do?method=login"), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await EnsureLoginFormAsync(page);

        await FillByNameAsync(page, "login", credentials.Login);
        await FillByNameAsync(page, "senha", credentials.Password);
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
        return pageContainsCnpj && copyMatch.Success
            ? new CustomerLookup(copyMatch.Groups["code"].Value.Trim(), "Cliente UNO", normalizedCnpj)
            : null;
    }

    private async Task FillByNameAsync(IPage page, string name, string value, bool dispatchChange = true)
    {
        var locator = FindEditableField(page, name);
        if (locator is null)
        {
            _logger.LogDebug("Campo {Field} nao encontrado/editavel na tela atual do UNO.", name);
            return;
        }

        await locator.EvaluateAsync(
            @"(element, payload) => {
                element.value = payload.value;
                if (payload.dispatchChange) {
                    element.dispatchEvent(new Event('change', { bubbles: true }));
                }
            }",
            new { value, dispatchChange });
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
                "(element, fieldValue) => { element.value = fieldValue; element.dispatchEvent(new Event('change', { bubbles: true })); }",
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
            var locator = frame.Locator($"input[name='{name}'], textarea[name='{name}'], select[name='{name}'], input[id='{name}'], textarea[id='{name}'], select[id='{name}']").First;
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
            var locator = frame.Locator($"input[name='{name}'], textarea[name='{name}'], select[name='{name}'], input[id='{name}'], textarea[id='{name}'], select[id='{name}']").First;
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

    private static IEnumerable<IFrame> OrderFramesForSubmit(IPage page, string? preferredFieldName)
    {
        return string.IsNullOrWhiteSpace(preferredFieldName)
            ? page.Frames
            : page.Frames.OrderByDescending(frame => FrameContainsField(frame, preferredFieldName));
    }

    private static bool FrameContainsField(IFrame frame, string fieldName)
    {
        try
        {
            return frame.Locator($"input[name='{fieldName}'], textarea[name='{fieldName}'], select[name='{fieldName}'], input[id='{fieldName}'], textarea[id='{fieldName}'], select[id='{fieldName}']").CountAsync().GetAwaiter().GetResult() > 0;
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

    private static async Task<(string Code, string Html)> WaitForOccurrenceCodeAsync(IPage page)
    {
        var html = string.Empty;
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            html = await GetAllFramesContentAsync(page);
            var occurrenceCode = ExtractOccurrenceCode(html);
            if (!string.IsNullOrWhiteSpace(occurrenceCode))
            {
                return (occurrenceCode, html);
            }

            await page.WaitForTimeoutAsync(500);
        }

        return (string.Empty, html);
    }

    private static string ExtractOccurrenceCode(string html)
    {
        var value = ExtractInputValue(html, "codOcorrencia");
        var digits = Digits(value);
        return digits.Length >= 2 && digits != "0001" ? digits : string.Empty;
    }

    private static string ExtractInputValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $"name=\"{Regex.Escape(name)}\"(?=[\\s>])[^>]*value=\"([^\"]*)\"",
            RegexOptions.IgnoreCase);

        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
    }

    private static string ExtractControllerMessage(string html)
    {
        var match = Regex.Match(
            html,
            "id=\"divMensagens\"[^>]*>(.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success ? CleanHtml(match.Groups[1].Value) : string.Empty;
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
                parts.Add($"<!-- FRAME name='{WebUtility.HtmlEncode(frame.Name)}' url='{WebUtility.HtmlEncode(frame.Url)}' -->\n{content}");
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                parts.Add($"<!-- FRAME name='{WebUtility.HtmlEncode(frame.Name)}' url='{WebUtility.HtmlEncode(frame.Url)}' unavailable='{WebUtility.HtmlEncode(ex.Message)}' -->");
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

    private string? GetConfigurationError(UnoCredentials? credentials)
    {
        return string.IsNullOrWhiteSpace(_options.BaseUrl)
            || credentials is null
            || string.IsNullOrWhiteSpace(credentials.Login)
            || string.IsNullOrWhiteSpace(credentials.Password)
            ? "Configure o login e a senha do UNO no aplicativo antes de finalizar a ocorrência."
            : null;
    }

    private UnoCredentials? ResolveCredentials(string? requestLogin, string? requestPassword)
    {
        var hasRequestLogin = !string.IsNullOrWhiteSpace(requestLogin);
        var hasRequestPassword = !string.IsNullOrWhiteSpace(requestPassword);
        return hasRequestLogin && hasRequestPassword
            ? new UnoCredentials(requestLogin!.Trim(), requestPassword!)
            : null;
    }

    private static SemaphoreSlim GetBrowserLock(string login)
    {
        var key = login.Trim();
        return BrowserLocksByLogin.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }

    private string AbsoluteUrl(string path)
    {
        return new Uri(new Uri(EnsureTrailingSlash(_options.BaseUrl)), path).ToString();
    }

    private static bool TryParsePositiveCode(string? value, out int code, string? defaultValue = null)
    {
        var rawValue = string.IsNullOrWhiteSpace(value) ? defaultValue : value;
        return int.TryParse(rawValue?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code)
            && code > 0;
    }

    private static OccurrenceOpenResponseDto Failed(string status, string message)
    {
        return new OccurrenceOpenResponseDto(status, message, null, null, null, null, null);
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

    private static string Digits(string? value)
    {
        return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
    }

    private static string EnsureTrailingSlash(string value)
    {
        return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    }

    private sealed record CustomerLookup(string Code, string Name, string? Cnpj);

    private sealed class UnoDialogCapture : IDisposable
    {
        private readonly IPage _page;
        private readonly TaskCompletionSource<string?> _dialogHandled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string? _latestMessage;

        public UnoDialogCapture(IPage page)
        {
            _page = page;
            _page.Dialog += HandleDialog;
        }

        public async Task<string?> WaitForMessageAsync(int timeoutMs)
        {
            var completed = await Task.WhenAny(_dialogHandled.Task, Task.Delay(timeoutMs));
            return completed == _dialogHandled.Task
                ? await _dialogHandled.Task
                : _latestMessage;
        }

        public void Dispose()
        {
            _page.Dialog -= HandleDialog;
        }

        private async void HandleDialog(object? sender, IDialog dialog)
        {
            _latestMessage = dialog.Message;
            _dialogHandled.TrySetResult(_latestMessage);
            await dialog.AcceptAsync();
        }
    }

    private sealed class UnoSessionEndedException : InvalidOperationException
    {
        public UnoSessionEndedException()
            : base("Sessao do UNO encerrada durante a automacao.")
        {
        }
    }

    private sealed record UnoCredentials(string Login, string Password);
}
