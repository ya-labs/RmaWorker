using System.Text.RegularExpressions;
using RmaWorker.Interfaces;

namespace RmaWorker.Services;

public sealed class EmailBodyCleaner : IEmailBodyCleaner
{
    private static readonly Regex[] HistoryMarkers =
    [
        new(@"(?im)^\s*Segue informa[cç][oõ]es para abertura do RMA de manuten[cç][aã]o\.?\s*$"),
        new(@"(?im)^\s*Recebemos a solicita[cç][aã]o de RMA, por[eé]m precisamos corrigir ou validar os pontos abaixo antes de prosseguir:\s*$"),
        new(@"(?im)^\s*-{2,}\s*Forwarded message\s*-{2,}\s*$"),
        new(@"(?im)^\s*De:\s+.+$"),
        new(@"(?im)^\s*From:\s+.+$"),
        new(@"(?im)^\s*Em .+ escreveu:\s*$"),
        new(@"(?im)^\s*On .+ wrote:\s*$")
    ];

    public string ExtractCurrentMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var currentBody = body;
        foreach (var marker in HistoryMarkers)
        {
            var match = marker.Match(currentBody);
            if (match.Success)
            {
                currentBody = currentBody[..match.Index];
            }
        }

        return currentBody.Trim();
    }
}
