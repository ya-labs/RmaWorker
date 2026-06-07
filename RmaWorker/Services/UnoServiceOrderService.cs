using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
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

    private static readonly Regex TokenRegex = new(
        "name=\"org\\.apache\\.struts\\.taglib\\.html\\.TOKEN\"\\s+value=\"([^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CustomerRowRegex = new(
        @"<td[^>]*>\s*&nbsp;(?<code>\d+)</td>\s*<td[^>]*>\s*&nbsp;(?<name>.*?)</td>\s*<td[^>]*>\s*&nbsp;(?<cnpj>[\d./-]+)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex CreatedItemRegex = new(
        "name=\"codItem\"[^>]*value=\"(?<code>\\d+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ExistingItemRegex = new(
        @"(?:carregarItem|copiar)\s*\(\s*'?(?<code>\d+)'?",
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

        if (string.IsNullOrWhiteSpace(_options.Login) || string.IsNullOrWhiteSpace(_options.Password))
        {
            return new RmaServiceOrderResponseDto(
                "UNO_CONFIG_INCOMPLETA",
                "Configure UnoErp__Login e UnoErp__Password para abrir a O.S no UNO.",
                []);
        }

        using var client = CreateClient();

        try
        {
            await LoginAsync(client, cancellationToken);

            var customer = await FindCustomerAsync(client, request.Cnpj, cancellationToken);
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
                results.Add(await OpenItemServiceOrderAsync(client, customer, request.Cnpj, item, cancellationToken));
            }

            var failed = results.Where(result => result.Status != "OS_ABERTA").ToList();
            return failed.Count == 0
                ? new RmaServiceOrderResponseDto("OS_ABERTA", "O.S aberta no UNO.", results)
                : new RmaServiceOrderResponseDto("OS_PARCIAL", "Uma ou mais O.S nao foram abertas no UNO.", results);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            _logger.LogError(ex, "Falha ao abrir O.S no UNO.");
            return new RmaServiceOrderResponseDto("UNO_ERRO", $"Falha ao abrir O.S no UNO: {ex.Message}", []);
        }
    }

    private HttpClient CreateClient()
    {
        var cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.All
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri(EnsureTrailingSlash(_options.BaseUrl)),
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds)
        };
    }

    private async Task LoginAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var loginPage = await GetStringAsync(client, string.Empty, cancellationToken);
        var token = ExtractToken(loginPage);

        var loginForm = new List<KeyValuePair<string, string>>
        {
            Token(token),
            Token(token),
            new("email", string.Empty),
            new("login", _options.Login),
            new("senha", _options.Password)
        };

        var result = await PostStringAsync(client, "sgw0001.do?method=validarLogin", loginForm, cancellationToken);
        if (!result.Contains("desktop", StringComparison.OrdinalIgnoreCase)
            && !result.Contains("UNO ERP", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Login no UNO nao retornou a tela esperada.");
        }

        await GetStringAsync(client, "desktop.do?method=preparar", cancellationToken);
    }

    private async Task<CustomerLookup?> FindCustomerAsync(HttpClient client, string cnpj, CancellationToken cancellationToken)
    {
        var page = await GetStringAsync(client, "cdq0101.do?method=prepListar", cancellationToken);
        var token = ExtractToken(page);
        var body = await PostStringAsync(client, "cdq0101.do?method=listar", new[]
        {
            Token(token),
            Token(token),
            new KeyValuePair<string, string>("clienteFinal", string.Empty),
            new("indice", string.Empty),
            new("codCliente", string.Empty),
            new("nomeCliente", string.Empty),
            new("situacao", string.Empty),
            new("cidade", string.Empty),
            new("razaoSocial", string.Empty),
            new("nomeContato", string.Empty),
            new("obsContato", string.Empty),
            new("emailContato", string.Empty),
            new("codClienteMatriz", string.Empty),
            new("inscEstadual", string.Empty),
            new("cnpj", Digits(cnpj)),
            new("cpf", string.Empty)
        }, cancellationToken);

        var match = CustomerRowRegex.Match(body);
        if (!match.Success)
        {
            return null;
        }

        return new CustomerLookup(
            WebUtility.HtmlDecode(match.Groups["code"].Value.Trim()),
            CleanHtml(match.Groups["name"].Value),
            WebUtility.HtmlDecode(match.Groups["cnpj"].Value.Trim()));
    }

    private async Task<RmaServiceOrderItemResultDto> OpenItemServiceOrderAsync(
        HttpClient client,
        CustomerLookup customer,
        string cnpj,
        RmaServiceOrderItemRequestDto item,
        CancellationToken cancellationToken)
    {
        var serialValidation = await _serialValidationService.ValidateAsync(item.Serial, cancellationToken);
        if (!serialValidation.Exists)
        {
            return BuildResult(item.Serial, cnpj, customer.Name, null, null, item.DefectReported, false, null, false, "SERIAL_NAO_ENCONTRADO", "Serial nao encontrado na consulta atual do UNO.", null);
        }

        var warrantyUntil = serialValidation.InvoiceIssuedAt?.AddYears(1);
        var isUnderWarranty = warrantyUntil.HasValue && warrantyUntil.Value >= DateOnly.FromDateTime(DateTime.Today);
        var categoryCode = isUnderWarranty ? WarrantyCategoryCode : OutOfWarrantyCategoryCode;
        var categoryDescription = isUnderWarranty ? "Garantia manutencao" : "Fora de garantia manutencao";

        var osPage = await GetStringAsync(client, "osw0001.do?method=prepTela", cancellationToken);
        var os = UnoOsForm.FromHtml(osPage);
        os.CodCliente = customer.Code;

        osPage = await PostStringAsync(client, "osw0001.do?method=buscarCliente", os.ToMainForm(), cancellationToken);
        os = os.Merge(UnoOsForm.FromHtml(osPage));

        var itemCode = await FindItemAsync(client, customer.Code, serialValidation.Serial, cancellationToken);
        if (string.IsNullOrWhiteSpace(itemCode))
        {
            itemCode = await CreateItemAsync(client, customer.Code, serialValidation, cancellationToken);
        }

        os.CodItem = itemCode;
        osPage = await PostStringAsync(client, "osw0001.do?method=buscarItem", os.ToMainForm(), cancellationToken);
        os = os.Merge(UnoOsForm.FromHtml(osPage));

        os.Ccusto = CostCenterCode.ToString(CultureInfo.InvariantCulture);
        osPage = await PostStringAsync(client, "osw0001.do?method=buscarCCusto", os.ToMainForm(), cancellationToken);
        os = os.Merge(UnoOsForm.FromHtml(osPage));

        var today = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("pt-BR"));
        var defect = string.IsNullOrWhiteSpace(item.DefectReported) ? "Defeito relatado pelo cliente." : item.DefectReported;
        var dataForm = BuildServiceDataForm(os.Token, categoryCode, today, defect);

        await PostStringAsync(client, "osw0001.do?method=buscarCategoria", dataForm, cancellationToken);
        await PostStringAsync(client, "osw0001.do?method=gravarDados", dataForm, cancellationToken);
        await PostStringAsync(client, "osw0001.do?method=gravarDados&cmd=gravar", dataForm, cancellationToken);

        os.Qtd = "1,00";
        var finalHtml = await PostStringAsync(client, "osw0001.do?method=gravar", os.ToMainForm(), cancellationToken);
        var serviceOrderCode = ExtractInputValue(finalHtml, "codOs");

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
            string.IsNullOrWhiteSpace(serviceOrderCode) ? null : serviceOrderCode);
    }

    private async Task<string?> FindItemAsync(HttpClient client, string customerCode, string serial, CancellationToken cancellationToken)
    {
        var page = await GetStringAsync(client, $"eqq0008.do?method=prepListar&codCliente={Uri.EscapeDataString(customerCode)}&fixaCliente=true&codPlano=", cancellationToken);
        var token = ExtractToken(page);
        var body = await PostStringAsync(client, "eqq0008.do?method=listar", new[]
        {
            Token(token),
            Token(token),
            new KeyValuePair<string, string>("codItem", string.Empty),
            new("codProduto", string.Empty),
            new("descricao", string.Empty),
            new("nrSerie", serial),
            new("nrPatrimonio", string.Empty),
            new("nomeVendedor", string.Empty),
            new("situacao", "1")
        }, cancellationToken);

        var match = ExistingItemRegex.Match(body);
        if (match.Success)
        {
            return match.Groups["code"].Value;
        }

        return body.Contains("Encontrados 0", StringComparison.OrdinalIgnoreCase)
            ? null
            : null;
    }

    private async Task<string> CreateItemAsync(
        HttpClient client,
        string customerCode,
        SerialValidationResultDto serialValidation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serialValidation.ProductCode))
        {
            throw new InvalidOperationException($"Consulta do serial {serialValidation.Serial} nao retornou codigo do produto.");
        }

        var page = await GetStringAsync(client, $"eqw0017.do?method=prepTela&codCliente={Uri.EscapeDataString(customerCode)}&fixaCliente=true", cancellationToken);
        var token = ExtractToken(page);
        var body = await PostStringAsync(client, "eqw0017.do?method=gravar", new[]
        {
            Token(token),
            Token(token),
            new KeyValuePair<string, string>("origem", "2"),
            new("uploadSubFolders", string.Empty),
            new("uploadRootPath", string.Empty),
            new("codItem", string.Empty),
            new("codCliente", customerCode),
            new("dtInstalacao", string.Empty),
            new("dtProximaMP", string.Empty),
            new("dtUltimaMP", string.Empty),
            new("codPlano", string.Empty),
            new("situacao", "1"),
            new("codProduto", serialValidation.ProductCode),
            new("descComercial", serialValidation.ProductDescription ?? string.Empty),
            new("descTecnica", string.Empty),
            new("observacao", string.Empty),
            new("qtd", "1"),
            new("nrSerie", serialValidation.Serial),
            new("nrPatrimonio", string.Empty),
            new("vendedorItem", string.Empty)
        }, cancellationToken);

        var match = CreatedItemRegex.Match(body);
        if (!match.Success)
        {
            throw new InvalidOperationException($"UNO nao retornou codItem apos criar o item do serial {serialValidation.Serial}.");
        }

        return match.Groups["code"].Value;
    }

    private static IReadOnlyCollection<KeyValuePair<string, string>> BuildServiceDataForm(
        string token,
        int categoryCode,
        string today,
        string defect)
    {
        return
        [
            Token(token),
            new("enviarEmailCliente", string.Empty),
            new("perguntarEmailCliente", string.Empty),
            new("codStatus", "10"),
            new("codCategoria", categoryCode.ToString(CultureInfo.InvariantCulture)),
            new("modalidade", "1"),
            new("tipoOs", "1"),
            new("codAtendente", AttendantCode.ToString(CultureInfo.InvariantCulture)),
            new("codResponsavel", string.Empty),
            new("dtAbertura", today),
            new("origem", "1"),
            new("dtPrevisaoConclusao", today),
            new("previsaoHoras", string.Empty),
            new("dtComprometida", today),
            new("hora", string.Empty),
            new("modoResposta", "4"),
            new("observacoes", string.Empty),
            new("prioridade", "100"),
            new("horaInicio", string.Empty),
            new("horaTermino", string.Empty),
            new("horaFechamento", string.Empty),
            new("dtAgendamento", string.Empty),
            new("horaInicioAgendamento", string.Empty),
            new("defeitoRelatado", defect),
            new("causaDefeito", string.Empty),
            new("defeitoConstatado", string.Empty),
            new("motivo", "1"),
            new("solucaoDefeito", string.Empty),
            new("codStatusDefeito", "1")
        ];
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

    private static async Task<string> GetStringAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(url, cancellationToken);
        await EnsureUnoSuccessAsync(response, $"GET {UrlLabel(url)}", cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task<string> PostStringAsync(
        HttpClient client,
        string url,
        IEnumerable<KeyValuePair<string, string>> form,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync(url, content, cancellationToken);
        await EnsureUnoSuccessAsync(response, $"POST {UrlLabel(url)}", cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static async Task EnsureUnoSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var preview = CleanHtml(body);
        if (preview.Length > 300)
        {
            preview = preview[..300];
        }

        throw new InvalidOperationException(
            $"{operation} retornou HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). {preview}");
    }

    private static string UrlLabel(string url)
    {
        return string.IsNullOrWhiteSpace(url) ? "/" : url;
    }

    private static KeyValuePair<string, string> Token(string value)
    {
        return new KeyValuePair<string, string>("org.apache.struts.taglib.html.TOKEN", value);
    }

    private static string ExtractToken(string html)
    {
        var match = TokenRegex.Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException("Token Struts nao encontrado na resposta do UNO.");
        }

        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string ExtractInputValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $"name=\"{Regex.Escape(name)}\"[^>]*value=\"([^\"]*)\"",
            RegexOptions.IgnoreCase);

        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
    }

    private static string ExtractDivValue(string html, string id)
    {
        var match = Regex.Match(
            html,
            $"id=\"{Regex.Escape(id)}\"[^>]*>(.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success ? CleanHtml(match.Groups[1].Value) : string.Empty;
    }

    private static string ExtractSelectedOptionValue(string html, string name)
    {
        var select = Regex.Match(
            html,
            $"<select[^>]*name=\"{Regex.Escape(name)}\"[^>]*>(.*?)</select>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!select.Success)
        {
            return string.Empty;
        }

        var option = Regex.Match(
            select.Groups[1].Value,
            "<option[^>]*value=\"([^\"]*)\"[^>]*(?:SELECTED|selected)[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return option.Success ? WebUtility.HtmlDecode(option.Groups[1].Value) : string.Empty;
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

    private sealed class UnoOsForm
    {
        public string Token { get; set; } = string.Empty;

        public string UserId { get; set; } = AttendantCode.ToString(CultureInfo.InvariantCulture);

        public string SessionId { get; set; } = string.Empty;

        public string CodEmpresa { get; set; } = "4";

        public string Corpo { get; set; } = "0001";

        public string CodCliente { get; set; } = string.Empty;

        public string CodContato { get; set; } = string.Empty;

        public string NomeContato { get; set; } = string.Empty;

        public string Ddd { get; set; } = string.Empty;

        public string Telefone { get; set; } = string.Empty;

        public string CodItem { get; set; } = string.Empty;

        public string Ccusto { get; set; } = string.Empty;

        public string Qtd { get; set; } = "1";

        public static UnoOsForm FromHtml(string html)
        {
            return new UnoOsForm
            {
                Token = TryExtractToken(html),
                UserId = ExtractInputValue(html, "userId"),
                SessionId = ExtractInputValue(html, "sessionId"),
                CodEmpresa = ExtractInputValue(html, "codEmpresa"),
                Corpo = ExtractInputValue(html, "corpo"),
                CodCliente = ExtractInputValue(html, "codCliente"),
                CodContato = FirstNonEmpty(ExtractInputValue(html, "codContato"), ExtractSelectedOptionValue(html, "codContato")),
                NomeContato = FirstNonEmpty(ExtractInputValue(html, "nomeContato"), ExtractDivValue(html, "nomeContato"), "MARCELO"),
                Ddd = ExtractInputValue(html, "ddd"),
                Telefone = ExtractInputValue(html, "telefone"),
                CodItem = ExtractInputValue(html, "codItem"),
                Ccusto = ExtractInputValue(html, "ccusto"),
                Qtd = FirstNonEmpty(ExtractInputValue(html, "qtd"), "1")
            };
        }

        public UnoOsForm Merge(UnoOsForm next)
        {
            Token = FirstNonEmpty(next.Token, Token);
            UserId = FirstNonEmpty(next.UserId, UserId);
            SessionId = FirstNonEmpty(next.SessionId, SessionId);
            CodEmpresa = FirstNonEmpty(next.CodEmpresa, CodEmpresa);
            Corpo = FirstNonEmpty(next.Corpo, Corpo);
            CodCliente = FirstNonEmpty(next.CodCliente, CodCliente);
            CodContato = FirstNonEmpty(next.CodContato, CodContato);
            NomeContato = FirstNonEmpty(next.NomeContato, NomeContato);
            Ddd = FirstNonEmpty(next.Ddd, Ddd);
            Telefone = FirstNonEmpty(next.Telefone, Telefone);
            CodItem = FirstNonEmpty(next.CodItem, CodItem);
            Ccusto = FirstNonEmpty(next.Ccusto, Ccusto);
            Qtd = FirstNonEmpty(next.Qtd, Qtd);
            return this;
        }

        public IReadOnlyCollection<KeyValuePair<string, string>> ToMainForm()
        {
            return
            [
                TokenValue(),
                TokenValue(),
                new("userId", UserId),
                new("sessionId", SessionId),
                new("codEmpresa", CodEmpresa),
                new("corpo", Corpo),
                new("emailNomeRemetente", string.Empty),
                new("nomeResponsavel", string.Empty),
                new("emailDestinatario", string.Empty),
                new("emailAssunto", string.Empty),
                new("emailMensagem", string.Empty),
                new("emailSMTP", string.Empty),
                new("linguagem", "pt_BR"),
                new("corpo", Corpo),
                new("codRecebimento", string.Empty),
                new("codItem", CodItem),
                new("msgConfirmaCopiaOS", "Confirma a copia da OS?"),
                new("codOs", string.Empty),
                new("codAtendimento", string.Empty),
                new("codCliente", CodCliente),
                new("codContato", CodContato),
                new("nomeContato", NomeContato),
                new("ddd", Ddd),
                new("telefone", Telefone),
                new("ramal", string.Empty),
                new("codPlano", string.Empty),
                new("codOportunidade", string.Empty),
                new("codOsCliente", string.Empty),
                new("ccusto", Ccusto),
                new("nfe", string.Empty),
                new("qtd", Qtd)
            ];
        }

        private KeyValuePair<string, string> TokenValue()
        {
            return new KeyValuePair<string, string>("org.apache.struts.taglib.html.TOKEN", Token);
        }

        private static string TryExtractToken(string html)
        {
            var match = TokenRegex.Match(html);
            return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : string.Empty;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
