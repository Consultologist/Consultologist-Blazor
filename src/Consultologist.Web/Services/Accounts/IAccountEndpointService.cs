namespace Consultologist.Web.Services.Accounts;

public interface IAccountEndpointService
{
    Task<AccountMeResponse> GetCurrentAccountAsync();
    Task<string> StartLinkedInLinkAsync();

    /// <summary>#195: disconnect this account's LinkedIn identity.</summary>
    Task DisconnectLinkedInAsync();
    Task SetDeliveryPasswordAsync(string password);
    Task ClearDeliveryPasswordAsync();

    /// <summary>#486: send a confirmation code to the address. Throws with the server's named reason.</summary>
    Task StartDeliveryAddressAsync(string address);

    /// <summary>#486: confirm the pending address with the code. Throws with the server's named reason.</summary>
    Task ConfirmDeliveryAddressAsync(string code);

    /// <summary>#486: remove the confirmed and any pending address.</summary>
    Task ClearDeliveryAddressAsync();

    /// <summary>#517: take the signed-in email as the delivery address — an organisation's token only. Throws with the server's named reason.</summary>
    Task UseSignedInDeliveryAddressAsync();
    Task<AccountSettingResponse?> GetSettingAsync(string key);
    Task SaveSettingAsync(string key, string value, string contentType);
    Task DeleteSettingAsync(string key);
    Task<AccountJobsResponse> GetJobsAsync(int limit = 20, string? continuationToken = null);
}
