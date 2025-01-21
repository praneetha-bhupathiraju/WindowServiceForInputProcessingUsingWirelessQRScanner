using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;

namespace HIDScannerService
{
    public class Worker : BackgroundService
    {
        private const string PublicDirectory = @"C:\Users\Public";
        private readonly string LogFilePath = Path.Combine(PublicDirectory, "scanned.txt");
        private readonly string ExcelFilePath = Path.Combine(PublicDirectory, "ScannedData.xlsx");
        private readonly string HashFilePath = Path.Combine(PublicDirectory, "ProcessedHashes.txt");
        private readonly string ErrorLogFilePath = Path.Combine(PublicDirectory, "ErrorLogs.txt"); // Error log file

        private readonly ILogger<Worker> _logger;
        private readonly HashSet<string> processedHashes = new HashSet<string>();

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;

            // Ensure directories and files exist
            EnsureFileExists(LogFilePath);
            EnsureFileExists(ExcelFilePath);
            EnsureFileExists(HashFilePath);
            EnsureFileExists(ErrorLogFilePath);

            // Preload hashes from Excel and persistent store
            PreloadProcessedData();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Service started. Monitoring scanned.txt...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (File.Exists(LogFilePath))
                    {
                        var lines = await File.ReadAllLinesAsync(LogFilePath, stoppingToken);

                        if (lines.Length > 0)
                        {
                            AppendToExcel(lines);

                            // Clear the file after processing
                            File.WriteAllText(LogFilePath, string.Empty);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while processing the log file.");
                }

                await Task.Delay(5000, stoppingToken); // Check every 5 seconds
            }
        }

        private void EnsureFileExists(string filePath)
        {
            try
            {
                string directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(filePath))
                {
                    File.Create(filePath).Dispose(); // Create the file if it doesn't exist
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error ensuring file exists: {filePath}");
            }
        }

        private void PreloadProcessedData()
        {
            try
            {
                // Load hashes from Excel file
                if (File.Exists(ExcelFilePath))
                {
                    FileInfo fileInfo = new FileInfo(ExcelFilePath);
                    using (var package = new ExcelPackage(fileInfo))
                    {
                        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                        if (worksheet != null)
                        {
                            int totalRows = worksheet.Dimension?.End.Row ?? 0;

                            for (int row = 2; row <= totalRows; row++) // Skip header row
                            {
                                string concatenatedRowData = string.Join("",
                                    Enumerable.Range(1, worksheet.Dimension.End.Column)
                                              .Select(col => worksheet.Cells[row, col].Text.Trim()));
                                string hash = ComputeHash(concatenatedRowData);
                                processedHashes.Add(hash);
                            }
                        }
                    }
                }

                // Load hashes from persistent hash file
                if (File.Exists(HashFilePath))
                {
                    var hashesFromFile = File.ReadAllLines(HashFilePath);
                    foreach (var hash in hashesFromFile)
                    {
                        processedHashes.Add(hash);
                    }
                }

                _logger.LogInformation("Preloaded {0} processed hashes.", processedHashes.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preloading processed data.");
            }
        }

        private void AppendToExcel(string[] lines)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(ExcelFilePath);

                using (var package = new ExcelPackage(fileInfo))
                {
                    // Get or create the worksheet
                    var worksheet = package.Workbook.Worksheets.Count == 0
                        ? package.Workbook.Worksheets.Add("Scanned Data")
                        : package.Workbook.Worksheets[0];

                    // Add headers if they do not exist
                    if (worksheet.Dimension == null || worksheet.Cells[1, 1].Value == null)
                    {
                        AddHeaders(worksheet);
                    }

                    // Determine the starting row for new data
                    int startRow = worksheet.Dimension?.End.Row + 1 ?? 2;

                    // Load existing Student IDs into a HashSet for quick duplicate detection
                    HashSet<string> existingStudentIds = new HashSet<string>();
                    if (worksheet.Dimension != null)
                    {
                        int totalRows = worksheet.Dimension.End.Row;
                        for (int row = 2; row <= totalRows; row++) // Skip header row
                        {
                            string studentId = worksheet.Cells[row, 3]?.Text.Trim(); // Assuming "Student ID" is in column 3
                            if (!string.IsNullOrWhiteSpace(studentId))
                            {
                                existingStudentIds.Add(studentId);
                            }
                        }
                    }

                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            // Split the line by apostrophes
                            string[] splitData = line.Split("'");

                            // Ignore the first element if it's empty
                            if (splitData.Length > 1 && !string.IsNullOrWhiteSpace(splitData[1]))
                            {
                                // Get the Student ID from the data (assuming column 3)
                                string studentId = splitData.Length > 3 ? splitData[3].Trim() : string.Empty;

                                if (string.IsNullOrWhiteSpace(studentId))
                                {
                                    _logger.LogWarning("Student ID missing for line: {0}. Skipping.", line);
                                    continue;
                                }

                                // Check for duplicates based on Student ID
                                if (existingStudentIds.Contains(studentId))
                                {
                                    string duplicateMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - Duplicate Student ID detected: {studentId}";
                                    File.AppendAllText(ErrorLogFilePath, duplicateMessage + Environment.NewLine); // Log to error file
                                    _logger.LogInformation(duplicateMessage);
                                    continue;
                                }

                                // Add Student ID to the existing IDs set
                                existingStudentIds.Add(studentId);

                                // Place the second element (Examiner) in the first column
                                worksheet.Cells[startRow, 1].Value = splitData[1].Trim();

                                // Loop over the remaining elements and place them into subsequent columns
                                for (int col = 2; col <= splitData.Length && col < 21; col++) // Max 20 columns
                                {
                                    if (col < splitData.Length)
                                    {
                                        string value = splitData[col].Trim();

                                        if (col == 14 && value.Equals("false", StringComparison.OrdinalIgnoreCase))
                                        {
                                            worksheet.Cells[startRow, col].Value = value;

                                            // Clear values for "Private Email", "Postal Address", and "Phone"
                                            worksheet.Cells[startRow, 12].Value = null; // Private Email
                                            worksheet.Cells[startRow, 13].Value = null; // Phone
                                            worksheet.Cells[startRow, 15].Value = null; // Postal Address
                                        }
                                        else
                                        {
                                            worksheet.Cells[startRow, col].Value = value;
                                        }
                                    }
                                }

                                // Move to the next row for the next line of data
                                startRow++;
                            }
                        }
                    }

                    // Save the Excel file
                    package.Save();
                }

                _logger.LogInformation("Data appended to Excel successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while appending data to Excel.");
            }
        }

        private void AddHeaders(ExcelWorksheet worksheet)
        {
            string[] headers = {
                "Examiner", "Second Reviewer", "Student ID", "Surname", "First name", "Field of study",
                "Degree", "Examination Regulations", "Title", "Start Date", "Uni account",
                "Private Email", "Phone", "Storage in alumni directory", "Postal Address",
                "Language", "Birthdate", "Birthplace", "zugelassen", "Additional Comments"
            };

            for (int col = 0; col < headers.Length; col++)
            {
                worksheet.Cells[1, col + 1].Value = headers[col];
            }

            using (var headerRange = worksheet.Cells[1, 1, 1, headers.Length])
            {
                headerRange.Style.Font.Bold = true;
            }
        }

        private string ComputeHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return Convert.ToBase64String(bytes);
            }
        }
    }
}