using System.Globalization;
using System.Net;
using System.Text;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;

namespace RmaWorker.Services;

public sealed class EmailResponseService : IEmailResponseService
{
    private readonly IGmailService _gmailService;

    public EmailResponseService(IGmailService gmailService)
    {
        _gmailService = gmailService;
    }

    public Task ReplyMissingDataAsync(
        EmailMessageDto message,
        IReadOnlyCollection<string> missingFields,
        CancellationToken cancellationToken)
    {
        return _gmailService.SendReplyAsync(message, BuildMissingDataBody(missingFields), cancellationToken);
    }

    public Task ReplySerialNotFoundAsync(
        EmailMessageDto message,
        string serial,
        CancellationToken cancellationToken)
    {
        return _gmailService.SendReplyAsync(message, BuildSerialNotFoundBody(serial), cancellationToken);
    }

    public Task ReplyRmaEligibleAsync(
        EmailMessageDto message,
        SerialValidationResultDto serialValidation,
        InvoiceDataDto? invoiceData,
        bool isUnderWarranty,
        DateOnly? warrantyUntil,
        CancellationToken cancellationToken)
    {
        var result = new RmaProcessingResultDto(
            new OllamaRmaExtractionDto(serialValidation.Serial, serialValidation.Cnpj, null, null, null, false, false, true, true, true),
            "APTO",
            null,
            [],
            new RmaTechnicalClassificationDto("APTO_PARA_ORIENTACAO_NF", "Fluxo legado de resposta apta.", []),
            serialValidation,
            invoiceData,
            isUnderWarranty,
            warrantyUntil);

        return _gmailService.SendHtmlReplyAsync(message, BuildRmaEligibleHtml([result], []), cancellationToken);
    }

    public Task ReplyProcessingResultsAsync(
        EmailMessageDto message,
        IReadOnlyCollection<RmaProcessingResultDto> results,
        CancellationToken cancellationToken)
    {
        var response = BuildProcessingResponse(results);

        return response.IsHtml
            ? _gmailService.SendHtmlReplyAsync(message, response.ResponseBody, cancellationToken)
            : _gmailService.SendReplyAsync(message, response.ResponseBody, cancellationToken);
    }

    public RmaAssistantResponseDto BuildProcessingResponse(IReadOnlyCollection<RmaProcessingResultDto> results)
    {
        var eligibleResults = results
            .Where(result => result.Status == "APTO" && result.SerialValidation is not null)
            .ToList();
        var pendingResults = results
            .Where(result => result.Status != "APTO")
            .ToList();

        if (eligibleResults.Count > 0)
        {
            return new RmaAssistantResponseDto(
                "APTO",
                true,
                BuildRmaEligibleHtml(eligibleResults, pendingResults),
                results);
        }

        if (results.Count == 1)
        {
            var result = results.First();
            if (result.Status is "UNO_TIMEOUT" or "UNO_INDISPONIVEL")
            {
                return new RmaAssistantResponseDto(
                    result.Status,
                    false,
                    result.Reason ?? GetDisplayReason(result),
                    results);
            }

            if (result.MissingFields.Count > 0)
            {
                return new RmaAssistantResponseDto(
                    result.Status,
                    false,
                    BuildMissingDataBody(result.MissingFields),
                    results);
            }

            if (result.Status == "SERIAL_NAO_ENCONTRADO" && !string.IsNullOrWhiteSpace(result.Extraction.Serial))
            {
                return new RmaAssistantResponseDto(
                    result.Status,
                    false,
                    BuildSerialNotFoundBody(result.Extraction.Serial),
                    results);
            }
        }

        var status = results.Count == 1 ? results.First().Status : "PENDENTE";
        return new RmaAssistantResponseDto(
            status,
            false,
            BuildPendingSummary(results),
            results);
    }

    private static string BuildMissingDataBody(IReadOnlyCollection<string> missingFields)
    {
        return $"""
            Ola, tudo bem?

            Obrigado pelo envio das informacoes. Para prosseguirmos com a analise da solicitacao de RMA, precisamos das seguintes informacoes: {string.Join(", ", missingFields)}

            Fico no aguardo.
            """;
    }

    private static string BuildSerialNotFoundBody(string serial)
    {
        return $"""
            Ola, tudo bem?

            Obrigado pelo envio das informacoes, porem nao encontramos o equipamento de numero de serie {serial} em nossa base de dados. Poderia verificar se esta correto, por favor?

            Fico no aguardo.
            """;
    }

    private static string BuildRmaEligibleHtml(
        IReadOnlyCollection<RmaProcessingResultDto> eligibleResults,
        IReadOnlyCollection<RmaProcessingResultDto> pendingResults)
    {
        var builder = new StringBuilder();
        builder.AppendLine("""<div style="font-family: Arial, Helvetica, sans-serif; font-size: 12px; line-height: 1.25; color: #000;">""");
        builder.AppendLine("Ola, tudo bem?<br>");
        builder.AppendLine("Segue informacoes para abertura do RMA de manutencao.<br><br>");
        builder.AppendLine("""<span style="color: #ff0000; font-weight: 700;">Atencao:</span><br>""");
        builder.AppendLine("""<span style="color: #ff0000;">1)</span> O RMA ainda <strong>NAO</strong> esta aberto! Favor encaminhar nota fiscal para o e-mail ( <a href="mailto:rma-notas@controlid.com.br" style="color: #0000ee; text-decoration: underline;">rma-notas@controlid.com.br</a> )<br>""");
        builder.AppendLine("""<span style="color: #ff0000;">2)</span> Nunca enviar equipamentos ou pecas sem um numero de RMA aberto (os produtos serao devolvidos ou sofrerao atrasos expressivos)<br>""");
        builder.AppendLine("""<span style="color: #ff0000;">3)</span> Destacar o numero de RMA no pacote enviado de forma clara<br>""");
        builder.AppendLine("""<span style="color: #ff0000;">4)</span> Nao nos responsabilizamos pelas demais pecas e acessorios enviados. Enviar somente o equipamento.<br>""");
        builder.AppendLine("""<span style="background-color: #ffff00;"><span style="color: #ff0000;">5)</span> <strong>Os equipamentos que forem enviados em notas separadas nao serao devolvidos na mesma coleta, apenas os equipamentos de uma mesma nota fiscal serao devolvidos juntos.</strong></span><br><br>""");
        builder.AppendLine("Orientacoes para a emissao da nota:<br><br>");
        builder.AppendLine("""<span style="color: #ff0000; font-weight: 700;">Natureza da operacao:</span> Remessa para Conserto<br><br>""");
        builder.AppendLine("""<span style="color: #ff0000; font-weight: 700;">DESTINATARIO:</span><br>""");
        builder.AppendLine("Razao Social : CONTROL ID IND. COM. DE HARDWARE E SERV. DE TECNOLOGIA LTDA<br>");
        builder.AppendLine("CNPJ : 08.238.299/0003-90<br>");
        builder.AppendLine("Inscricao Estadual 002531372.00-90<br>");
        builder.AppendLine("Endereco: RUA JOSEPHA GOMES DE SOUZA , 298 - GALPAO 02 e 03<br>");
        builder.AppendLine("BAIRRO : Distrito industrial Pires II<br>");
        builder.AppendLine("CEP: 37642-900<br>");
        builder.AppendLine("Municipio : Extrema - MG.<br>");
        builder.AppendLine("Telefone Control (11) 3059-9900<br><br>");

        var rmaIndex = 1;
        foreach (var result in eligibleResults)
        {
            AppendEligibleProduct(builder, result, rmaIndex);
            rmaIndex++;
        }

        builder.AppendLine("OBS: A nota fiscal de conserto <strong>NAO</strong> pode ter impostos destacados, por gentileza nao mencionar ICMS/IPI/PIS/COFINS.<br><br>");
        builder.AppendLine("""<span style="background-color: #ffff00;">OBS: Apos 30 dias sem resposta referente ao envio da Nota fiscal conserto/troca, o RMA em aberto para tal processo sera encerrado.</span>""");

        if (pendingResults.Count > 0)
        {
            builder.AppendLine("<br><br>");
            builder.AppendLine("""<span style="color: #ff0000; font-weight: 700;">Solicitacoes que precisam de correcao, detalhes ou testes:</span><br>""");
            foreach (var result in pendingResults)
            {
                AppendPendingResultHtml(builder, result);
            }
        }

        builder.AppendLine("</div>");
        return builder.ToString();
    }

    private static void AppendEligibleProduct(StringBuilder builder, RmaProcessingResultDto result, int rmaIndex)
    {
        var color = GetRmaColor(rmaIndex);
        builder.AppendLine($"""
            <div style="margin: 14px 0 10px 0; color: {color}; font-weight: 700;">
            ------------------------------------<br>
            RMA {rmaIndex} - Serie {Html(result.SerialValidation?.Serial)}<br>
            ------------------------------------
            </div>
            """);
        builder.AppendLine("""<span style="color: #ff0000; font-weight: 700;">DADOS DO PRODUTO/SERVICO:</span><br>""");
        builder.AppendLine($"NCM: {Html(ValueOrInvoiceFallback(result.Invoice?.Ncm))}<br>");
        builder.AppendLine("CFOP: 5915 - para Empresas dentro do Estado de Minas Gerais<br>");
        builder.AppendLine("CFOP: 6915 - para Empresas fora do Estado de Minas Gerais<br>");
        builder.AppendLine($"Descricao do produto: {Html(result.SerialValidation?.ProductDescription)}<br>");
        builder.AppendLine($"Valor unitario: {Html(ValueOrInvoiceFallback(FormatCurrency(result.Invoice?.UnitValue)))}<br><br>");
        builder.AppendLine("""<span style="color: #ff0000; font-weight: 700;">Informacoes que devem constar no campo Dados Adicionais:</span><br>""");
        builder.AppendLine($"<strong>N SERIE EQUIPAMENTO:</strong> {Html(result.SerialValidation?.Serial)}<br>");
        builder.AppendLine($"<strong>N NOTA DE VENDA:</strong> {Html(ValueOrInvoiceFallback(result.Invoice?.Number))}<br>");
        builder.AppendLine($"<strong>DATA DA NOTA:</strong> {Html(ValueOrInvoiceFallback(FormatDate(result.Invoice?.IssuedAt ?? result.SerialValidation?.InvoiceIssuedAt)))}<br><br>");
    }

    private static void AppendPendingResultHtml(StringBuilder builder, RmaProcessingResultDto result)
    {
        builder.AppendLine($"{Html(GetDisplaySerial(result))}: {Html(GetDisplayReason(result))}<br>");

        if (result.TechnicalClassification?.Instructions.Count > 0)
        {
            builder.AppendLine("<ul>");
            foreach (var instruction in result.TechnicalClassification.Instructions)
            {
                builder.AppendLine($"<li>{Html(instruction)}</li>");
            }
            builder.AppendLine("</ul>");
        }
    }

    private static string BuildPendingSummary(IReadOnlyCollection<RmaProcessingResultDto> results)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Ola, tudo bem?");
        builder.AppendLine();
        builder.AppendLine("Recebemos a solicitacao de RMA, porem precisamos corrigir ou validar os pontos abaixo antes de prosseguir:");
        builder.AppendLine();

        foreach (var result in results)
        {
            builder.AppendLine($"- {GetDisplaySerial(result)}: {GetDisplayReason(result)}");
            foreach (var instruction in result.TechnicalClassification?.Instructions ?? [])
            {
                builder.AppendLine($"  - {instruction}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Fico no aguardo.");
        return builder.ToString();
    }

    private static string GetDisplaySerial(RmaProcessingResultDto result)
    {
        return string.IsNullOrWhiteSpace(result.Extraction.Serial)
            ? "sem serial identificado"
            : $"serial {result.Extraction.Serial}";
    }

    private static string GetDisplayReason(RmaProcessingResultDto result)
    {
        if (result.MissingFields.Count > 0)
        {
            return $"faltam: {string.Join(", ", result.MissingFields)}";
        }

        return result.Status switch
        {
            "PRECISA_TESTES" => "precisamos dos testes/evidencias complementares antes de prosseguir com a orientacao de nota",
            "PRECISA_DETALHES" => "a descricao do defeito esta muito generica; precisamos de mais detalhes antes de prosseguir",
            "UNO_TIMEOUT" => "a consulta ao UNO demorou mais que o esperado; tente novamente em alguns instantes",
            "UNO_INDISPONIVEL" => "nao foi possivel consultar o UNO no momento; tente novamente em alguns instantes",
            _ => result.Reason ?? "nao apto para processamento"
        };
    }

    private static string GetRmaColor(int rmaIndex)
    {
        var colors = new[] { "#c00000", "#0057b8", "#b8860b", "#008060", "#7030a0" };
        return colors[(rmaIndex - 1) % colors.Length];
    }

    private static string Html(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private static string FormatCurrency(decimal? value)
    {
        return value.HasValue
            ? value.Value.ToString("N2", CultureInfo.GetCultureInfo("pt-BR"))
            : string.Empty;
    }

    private static string FormatDate(DateOnly? value)
    {
        return value.HasValue ? value.Value.ToString("dd/MM/yyyy") : string.Empty;
    }

    private static string ValueOrInvoiceFallback(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "preencher conforme nota fiscal de venda"
            : value;
    }
}
