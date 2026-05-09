namespace DLSSVersionToolkit.Core.Services;

using System.Globalization;
using System.IO;
using System.Text;
using DLSSVersionToolkit.Core.Models;

public interface IExportService
{
    void ExportToCsv(ScanResult result, string filePath);
    void ExportToJson(ScanResult result, string filePath);
}

public class ExportService : IExportService
{
    public void ExportToCsv(ScanResult result, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Source,BuildID,DLSS,FrameGen,DLSSD,DeepDVC,Streamline,Path,ScannedAt");

        foreach (var entry in result.Sources)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(entry.Source),
                EscapeCsv(entry.BuildID),
                EscapeCsv(entry.DLSS),
                EscapeCsv(entry.FrameGen),
                EscapeCsv(entry.DLSSD),
                EscapeCsv(entry.DeepDVC),
                EscapeCsv(entry.Streamline),
                EscapeCsv(entry.Path),
                entry.ScannedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            ));
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    public void ExportToJson(ScanResult result, string filePath)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };

        var export = new
        {
            ExportedAt = DateTime.UtcNow,
            ScanDuration = result.Duration.ToString(),
            Sources = result.Sources,
            NewestVersions = result.NewestPerComponent,
            Recommendations = result.Recommendations,
            Warnings = result.Warnings,
            Errors = result.Errors
        };

        var json = System.Text.Json.JsonSerializer.Serialize(export, options);
        File.WriteAllText(filePath, json, Encoding.UTF8);
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}