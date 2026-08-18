namespace Consultologist.Web.Services.Accounts;

public interface IAccountEndpointService
{
    Task<AccountMeResponse> GetCurrentAccountAsync();
    Task<string> StartLinkedInLinkAsync();

    /// <summary>#195: disconnect this account's LinkedIn identity.</summary>
    Task DisconnectLinkedInAsync();
    Task SetDeliveryPasswordAsync(string password);
    Task ClearDeliveryPasswordAsync();
    Task<AccountSettingResponse?> GetSettingAsync(string key);
    Task SaveSettingAsync(string key, string value, string contentType);
    Task DeleteSettingAsync(string key);
    Task<AccountJobsResponse> GetJobsAsync(int limit = 20, string? continuationToken = null);
}
