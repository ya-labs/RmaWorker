using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface IRmaTechnicalClassifier
{
    RmaTechnicalClassificationDto Classify(OllamaRmaExtractionDto extraction, string currentEmailBody);
}
