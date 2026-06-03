using RmaWorker.DTOs;

namespace RmaWorker.Interfaces;

public interface ISerialValidationService
{
    Task<SerialValidationResultDto> ValidateAsync(string serial, CancellationToken cancellationToken);
}
