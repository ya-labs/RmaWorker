namespace RmaWorker.Interfaces;

public interface ICnpjValidator
{
    bool IsValid(string? cnpj);
}
