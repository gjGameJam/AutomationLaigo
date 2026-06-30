using System.IO.Compression;

namespace LaigO.Tests.Diagnostics;

/// <summary>
/// Helpers that turn an artifact ZIP into a human-readable diagnostic string.
/// Extracted from the test body so the inspection logic (which can itself throw)
/// is reusable and cannot mask the assertion it was meant to explain.
/// </summary>
public static class ArtifactDiagnostics
{
    /// <summary>
    /// Describe the contents of an artifact ZIP: entry names + sizes, whether
    /// the order list is present, and the manifest text. Never throws — any
    /// failure to read the ZIP is folded into the returned string so the caller
    /// can attach it to an assertion message safely.
    /// </summary>
    public static async Task<string> DescribeAsync(byte[] zipBytes)
    {
        try
        {
            using var zipStream = new MemoryStream(zipBytes);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            var entries = string.Join(
                ", ",
                archive.Entries.Select(e => $"{e.FullName} ({e.Length}B)"));

            var hasOrderList = archive.GetEntry("OrderLists/order_list.json") != null;

            var manifestEntry = archive.GetEntry("manifest.json");
            string manifestText;
            if (manifestEntry is null)
            {
                manifestText = "(no manifest.json in zip)";
            }
            else
            {
                using var reader = new StreamReader(manifestEntry.Open());
                manifestText = await reader.ReadToEndAsync();
            }

            return $"Artifact ZIP entries: [{entries}]\n" +
                   $"ZIP contains OrderLists/order_list.json: {hasOrderList}\n" +
                   $"Manifest: {manifestText}";
        }
        catch (Exception ex)
        {
            return $"Artifact download/inspect failed: {ex.GetType().Name}: {ex.Message}";
        }
    }
}
