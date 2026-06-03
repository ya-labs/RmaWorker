using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RmaWorker.DTOs;
using RmaWorker.Interfaces;

namespace RmaWorker.Services;

public sealed class RmaTechnicalClassifier : IRmaTechnicalClassifier
{
    private static readonly string[] EvidenceTerms =
    [
        "anexo",
        "anexos",
        "video",
        "videos",
        "evidencia",
        "evidencias",
        "teste realizado",
        "testes realizados",
        "testado",
        "testamos",
        "outra fonte",
        "fonte diferente",
        "atualizacao",
        "atualizado",
        "factory reset",
        "reset de fabrica",
        "recovery",
        "firmware",
        "defeito persistiu",
        "problema persistiu",
        "falha persistiu",
        "persistiu"
    ];

    private static readonly string[] TestRequiredTerms =
    [
        "nao liga",
        "sem sinal de vida",
        "queimado",
        "queimada",
        "reiniciando",
        "reinicia",
        "esporadicamente",
        "intermitente",
        "tag",
        "tags",
        "cartao",
        "reconhecimento",
        "comunicacao",
        "travando",
        "lento",
        "listras",
        "tela",
        "leitura"
    ];

    private static readonly string[] VeryVagueDefects =
    [
        "defeito",
        "com defeito",
        "problema",
        "com problema",
        "nao funciona",
        "nao esta funcionando",
        "parou",
        "parou de funcionar",
        "falha"
    ];

    public RmaTechnicalClassificationDto Classify(OllamaRmaExtractionDto extraction, string currentEmailBody)
    {
        var defect = Normalize(extraction.Defeito ?? string.Empty);
        var text = Normalize(string.Join(
            " ",
            extraction.Defeito,
            extraction.Produto,
            extraction.GarantiaInformada,
            currentEmailBody));

        if (IsVeryVagueDefect(defect))
        {
            return new RmaTechnicalClassificationDto(
                "PRECISA_DETALHES",
                "Defeito muito generico para seguir com orientacao de RMA.",
                BuildDetailInstructions());
        }

        if (extraction.EvidenciasInformadas
            || extraction.TestesInformados
            || ContainsAny(text, EvidenceTerms))
        {
            return new RmaTechnicalClassificationDto(
                "APTO_PARA_ORIENTACAO_NF",
                "Cliente informou testes, evidencias ou anexos para continuidade da analise.",
                []);
        }

        if (ContainsAny(defect, TestRequiredTerms))
        {
            return new RmaTechnicalClassificationDto(
                "PRECISA_TESTES",
                "Defeito exige testes obrigatorios antes da orientacao de nota.",
                BuildInstructions(defect));
        }

        return new RmaTechnicalClassificationDto(
            "PRECISA_TESTES",
            "Testes obrigatorios precisam ser informados antes da orientacao de nota.",
            BuildInstructions(defect));
    }

    private static IReadOnlyCollection<string> BuildDetailInstructions()
    {
        return
        [
            "informar o comportamento exato apresentado pelo equipamento",
            "informar em qual momento a falha acontece",
            "informar quais testes ja foram realizados",
            "encaminhar video ou evidencia da falha, se disponivel"
        ];
    }

    private static IReadOnlyCollection<string> BuildInstructions(string defect)
    {
        var instructions = new List<string>
        {
            "encaminhar video demonstrando a falha apresentada",
            "informar os testes realizados e se o defeito persistiu"
        };

        if (ContainsAny(defect, ["nao liga", "sem sinal de vida", "queimado", "queimada", "reiniciando", "reinicia", "esporadicamente", "intermitente", "listras", "tela"]))
        {
            instructions.Add("testar o equipamento isolado, sem perifericos conectados");
            instructions.Add("testar com outra fonte 12V compativel, preferencialmente de no minimo 3A");
            instructions.Add("verificar se ha mau contato na alimentacao");
            instructions.Add("tentar acessar a tela de Recovery");
            instructions.Add("realizar Factory Reset and Update Firmware, caso o equipamento permita");
        }

        if (ContainsAny(defect, ["tag", "tags", "cartao", "leitura", "reconhecimento"]))
        {
            instructions.Add("testar com mais de uma TAG/cartao compativel");
            instructions.Add("confirmar se o tipo da TAG/cartao e compativel com o modelo do equipamento");
            instructions.Add("verificar se a forma de identificacao por TAG/cartao esta habilitada");
            instructions.Add("realizar atualizacao de firmware com restauracao de fabrica, se possivel");
        }

        return instructions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsVeryVagueDefect(string defect)
    {
        return defect.Length < 8 || VeryVagueDefects.Contains(defect, StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsAny(string source, IReadOnlyCollection<string> terms)
    {
        return terms.Any(term => source.Contains(Normalize(term), StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return Regex.Replace(builder.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim().ToLowerInvariant();
    }
}
