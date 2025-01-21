using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace HIDScannerApp
{
    public partial class MainWindow : Window
    {
        private const string PublicDirectory = @"C:\Users\Public";
        private readonly string LogFilePath = Path.Combine(PublicDirectory, "scanned.txt");
        private HashSet<string> scannedStudentIds = new HashSet<string>(); // Store scanned Student IDs
        private DispatcherTimer resetStatusTimer;

        public MainWindow()
        {
            InitializeComponent();
            EnsureFileExists(LogFilePath);
            LoadScannedStudentIds(); // Load already scanned IDs from file

            // Initialize the reset timer
            resetStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2) // Duration to reset status
            };
            resetStatusTimer.Tick += (s, args) =>
            {
                lblStatus.Content = "Ready for the next scan.";
                resetStatusTimer.Stop(); // Stop the timer after resetting the status
            };
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
                lblStatus.Content = $"Error ensuring file exists: {ex.Message}";
                StartResetTimer();
            }
        }

        private void LoadScannedStudentIds()
        {
            try
            {
                if (File.Exists(LogFilePath))
                {
                    var existingEntries = File.ReadAllLines(LogFilePath);
                    foreach (var entry in existingEntries)
                    {
                        string studentId = ExtractStudentId(CleanScannedData(entry));
                        if (!string.IsNullOrWhiteSpace(studentId))
                        {
                            scannedStudentIds.Add(studentId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lblStatus.Content = $"Error loading scanned IDs: {ex.Message}";
                StartResetTimer();
            }
        }

        private void TxtScannedData_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                try
                {
                    // Reset status to "Processing"
                    lblStatus.Content = "Processing...";

                    // Capture and clean the scanned data
                    string scannedData = txtScannedData.Text;
                    string cleanedData = CleanScannedData(scannedData);

                    // Extract Student ID
                    string studentId = ExtractStudentId(cleanedData);

                    if (string.IsNullOrWhiteSpace(cleanedData) || string.IsNullOrWhiteSpace(studentId))
                    {
                        lblStatus.Content = "Invalid data format. Scan again.";
                        StartResetTimer();
                    }
                    else if (scannedStudentIds.Contains(studentId))
                    {
                        lblStatus.Content = "Duplicate Student ID detected.";
                        Console.WriteLine($"Duplicate ID Detected: {studentId}");
                        Console.WriteLine($"Current IDs: {string.Join(", ", scannedStudentIds)}");
                        StartResetTimer();
                    }
                    else
                    {
                        // Add student ID to the list and save the data
                        scannedStudentIds.Add(studentId);
                        SaveToTextFile(cleanedData);
                        lblStatus.Content = "Data saved successfully!";
                        StartResetTimer();
                    }
                }
                catch (Exception ex)
                {
                    lblStatus.Content = $"Error: {ex.Message}";
                    StartResetTimer();
                }
                finally
                {
                    // Clear the TextBox and prepare for the next scan
                    txtScannedData.Clear();
                }
            }
        }

        private string CleanScannedData(string data)
        {
            // Replace tabs, newlines, and extra spaces with single spaces
            string cleanedData = Regex.Replace(data, @"\s+", " ").Trim();
            Console.WriteLine($"Cleaned Data: {cleanedData}"); // Log for debugging
            return cleanedData;
        }

        private string ExtractStudentId(string data)
        {
            // Use Regex to extract the first numeric sequence of 6 or more digits as the Student ID
            Match match = Regex.Match(data, @"'(\d{6,})'");
            if (match.Success)
            {
                string studentId = match.Groups[1].Value;
                Console.WriteLine($"Extracted Student ID: {studentId}"); // Log for debugging
                return studentId;
            }
            return null; // Return null if no valid Student ID is found
        }

        private void SaveToTextFile(string data)
        {
            try
            {
                File.AppendAllText(LogFilePath, data + Environment.NewLine);
            }
            catch (Exception ex)
            {
                lblStatus.Content = $"Error saving to text file: {ex.Message}";
                StartResetTimer();
            }
        }

        private void StartResetTimer()
        {
            // Start the timer to reset the status label after 2 seconds
            resetStatusTimer.Start();
        }
    }
}
