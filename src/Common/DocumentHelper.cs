using System;
using System.Text;
using System.Text.RegularExpressions;
using SIAP.Api.Entities;

namespace SIAP.Api.Common;

public static class DocumentHelper
{
    public static string CleanOcrNoise(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Basic cleanups for OCR
        text = Regex.Replace(text, @"(?m)^\s*-\s*\d+\s*-\s*$", ""); // Only remove standalone page numbers

        return text;
    }

    public static void EnrichDocumentMetadata(Document document, string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return;

        var fileName = document.NamaFile ?? string.Empty;
        var nameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(fileName);
        
        // Match format like "5. Laporan_Firman Muhamad Sahidin_Mei_2026"
        var match = Regex.Match(nameWithoutExtension, @"(?:^\d+\.\s*)?Laporan_([^_]+)_([A-Za-z]+)_(\d{4})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            if (string.IsNullOrWhiteSpace(document.NamaTenagaAhli))
            {
                document.NamaTenagaAhli = match.Groups[1].Value.Trim().ToUpperInvariant();
            }
            else
            {
                document.NamaTenagaAhli = document.NamaTenagaAhli.Trim().ToUpperInvariant();
            }
            
            if (string.IsNullOrWhiteSpace(document.PeriodeLaporan))
            {
                document.PeriodeLaporan = $"{match.Groups[2].Value.Trim()} {match.Groups[3].Value}";
            }
            
            if (string.IsNullOrWhiteSpace(document.Nama) || document.Nama == fileName)
            {
                document.Nama = $"Laporan {document.NamaTenagaAhli} - {document.PeriodeLaporan}";
            }
        }
        else 
        {
            // Fallback for other formats
            var fallbackMatch = Regex.Match(fileName, @"(0[1-9]|1[0-2])-20\d\d");
            if (fallbackMatch.Success && string.IsNullOrWhiteSpace(document.PeriodeLaporan))
            {
                document.PeriodeLaporan = fallbackMatch.Groups[0].Value;
            }
        }

        if (string.IsNullOrWhiteSpace(document.JenisDokumen))
        {
            var lower = fileName.ToLowerInvariant();
            if (lower.Contains("bulanan")) document.JenisDokumen = "Laporan Bulanan";
            else if (lower.Contains("akhir") || lower.Contains("final")) document.JenisDokumen = "Laporan Akhir";
            else if (lower.Contains("antara") || lower.Contains("progres") || lower.Contains("kemajuan")) document.JenisDokumen = "Laporan Antara";
            else if (lower.Contains("harian")) document.JenisDokumen = "Laporan Harian";
            else if (lower.Contains("mingguan")) document.JenisDokumen = "Laporan Mingguan";
            else if (lower.Contains("kak") || lower.Contains("tor")) document.JenisDokumen = "Kerangka Acuan Kerja (KAK)";
            else if (lower.Contains("bast") || lower.Contains("serah terima")) document.JenisDokumen = "Berita Acara (BAST)";
            else if (lower.Contains("teknis") || lower.Contains("spesifikasi")) document.JenisDokumen = "Dokumen Teknis";
            else document.JenisDokumen = "Laporan Kerja";
        }
    }
}
