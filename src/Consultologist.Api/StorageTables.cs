using Azure.Core;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;

namespace Consultologist.Api;

/// <summary>
/// One construction path for every table store (#10): Entra ID first — a
/// {section}:TableServiceUri setting plus the app's managed identity — with
/// the named-connection-string form as the local-dev (Azurite) fallback,
/// mirroring the workflow-package blob factory's posture.
/// </summary>
public static class StorageTables
{
    public static TableClient CreateClient(
        IConfiguration configuration,
        TokenCredential credential,
        string tableName,
        params string[] configSections)
    {
        foreach (var section in configSections)
        {
            var tableServiceUri = configuration[$"{section}:TableServiceUri"];

            if (!string.IsNullOrWhiteSpace(tableServiceUri))
            {
                return new TableClient(new Uri(tableServiceUri), tableName, credential);
            }
        }

        // #596: the middle rung — no explicit URI anywhere in the chain, but
        // the geography names the account (Storage__Region + the section→role
        // map). Explicit settings above stay the override; local dev without
        // a region falls through to the connection string below unchanged.
        foreach (var section in configSections)
        {
            var derivedUri = StorageAccounts.DerivedUriForSection(configuration, section, "table");

            if (derivedUri != null)
            {
                return new TableClient(new Uri(derivedUri), tableName, credential);
            }
        }

        var connectionStringName = configSections
            .Select(section => configuration[$"{section}:ConnectionStringName"])
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name))
            ?? "AzureWebJobsStorage";
        var connectionString = configuration[connectionStringName]
            ?? Environment.GetEnvironmentVariable(connectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Neither a TableServiceUri ({string.Join("/", configSections)}) nor connection string '{connectionStringName}' is configured for table '{tableName}'.");
        }

        return new TableClient(connectionString, tableName);
    }
}
