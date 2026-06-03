using System.Text.RegularExpressions;
using RmaWorker.Interfaces;

namespace RmaWorker.Validators;

public sealed class CnpjValidator : ICnpjValidator
{
    public bool IsValid(string? cnpj)
    {
        if (string.IsNullOrWhiteSpace(cnpj))
        {
            return false;
        }

        var digits = Regex.Replace(cnpj, @"\D", string.Empty);
        return Regex.IsMatch(digits, @"^\d{14}$")
            && !Regex.IsMatch(digits, @"^(\d)\1{13}$");
    }
}
