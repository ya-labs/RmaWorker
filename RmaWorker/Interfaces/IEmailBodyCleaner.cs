namespace RmaWorker.Interfaces;

public interface IEmailBodyCleaner
{
    string ExtractCurrentMessage(string body);
}
