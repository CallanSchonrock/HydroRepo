using System;
using System.Collections.Generic;
using System.Globalization;
using CsvHelper;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace Mydro
{
    class RainfallFile
    {
        public Dictionary<string, List<object>> subcatRainfall = new Dictionary<string, List<object>>();

        public class CsvRow
        {
            public required string Subcat { get; set; }
            public required string Depth { get; set; }
            public required string Row { get; set; }
            public required string Col { get; set; }
            public required string TemporalPattern { get; set; }
            public string Time { get; set; }
            public string Value { get; set; }
        }

        public List<(float Time, float Value)> GetTemporalPattern(string temporalFile, string time, string value, Dictionary<string, string> replaceDict)
        {
            List<(float Time, float Value)> pattern = new List<(float, float)>();
            if (!File.Exists(temporalFile))
            {
                Console.WriteLine($"Error: File does not exist at path {temporalFile}.");
                Environment.Exit(1);
            }
            using (var reader = new StreamReader(temporalFile))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                var records = csv.GetRecords<dynamic>().ToList();
                int row = 1;
                try
                {
                    if (replaceDict.ContainsKey("STARTDATE"))
                    {
                        DateTime startDate = DateTime.ParseExact(replaceDict["STARTDATE"], "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                        DateTime endDate = DateTime.ParseExact(replaceDict["ENDDATE"], "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

                        foreach (var record in records)
                        {

                            // Use reflection to access the property values by their names
                            var recordDict = (IDictionary<string, object>)record;
                            DateTime recordDate = DateTime.ParseExact(recordDict[time]?.ToString(), "dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                            if (recordDate > endDate)
                            {
                                return pattern;
                            }
                            // Directly parse the values as floats
                            float Time = (float)(recordDate - startDate).TotalHours; 
                            float Value = float.Parse(recordDict[value]?.ToString(), CultureInfo.InvariantCulture);

                            pattern.Add((Time, Value));
                            row++;
                        }
                    }
                    else
                    {
                        foreach (var record in records)
                        {

                            // Use reflection to access the property values by their names
                            var recordDict = (IDictionary<string, object>)record;
                            // Directly parse the values as floats
                            float Time = float.Parse(recordDict[time]?.ToString(), CultureInfo.InvariantCulture);
                            float Value = float.Parse(recordDict[value]?.ToString(), CultureInfo.InvariantCulture);

                            pattern.Add((Time, Value));
                            row++;
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"Error on Line: {row} of {temporalFile}"); Environment.Exit(0); }
            }

            return pattern;
        }

        public RainfallFile(string filePath, Dictionary<string, string> replaceDict)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: File does not exist at path {filePath}.");
                Environment.Exit(1);
            }
            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                var records = csv.GetRecords<CsvRow>().ToList();

                foreach (var record in records)
                {
                    record.Row = ReplacePlaceholders(record.Row, replaceDict);
                    record.Depth = ReplacePlaceholders(record.Depth, replaceDict);
                    var depthValue = GetDepthValue(ReplacePlaceholders(record.Depth, replaceDict), 
                        ReplacePlaceholders(record.Row, replaceDict), ReplacePlaceholders(record.Col, replaceDict));
                    var temporalPattern = GetTemporalPattern(ReplacePlaceholders(record.TemporalPattern, replaceDict),
                        ReplacePlaceholders(record.Time, replaceDict), ReplacePlaceholders(record.Value, replaceDict), replaceDict)
                                        .Select(tp => (Time: tp.Time * 3600f, Value: tp.Value))  // Convert Time to seconds
                                        .ToList();

                    var factoredTemporalPattern = new List<(float Time, float Intensity)>();

                    for (int i = 0; i < temporalPattern.Count; i++)
                    {
                        var current = temporalPattern[i];
                        var next = i < temporalPattern.Count - 1 ? temporalPattern[i + 1] : temporalPattern[i - 1];

                        // Calculate time interval in hours
                        float timeInterval;
                        if (i < temporalPattern.Count - 1)
                        {
                            timeInterval = next.Time - current.Time;
                        }
                        else
                        {
                            timeInterval = current.Time - temporalPattern[i - 1].Time;
                        }

                        // Calculate intensity (mm/hr) for the current timestep
                        float intensity = current.Value * depthValue / timeInterval;

                        factoredTemporalPattern.Add((Time: current.Time, Intensity: intensity));
                    }

                    // Save the result in your dictionary
                    subcatRainfall[record.Subcat] = new List<object> { factoredTemporalPattern };
                }
            }
        }

        public static string ReplacePlaceholders(string input, Dictionary<string, string> keyValuePairs)
        {
            // Regex pattern to find anything between angle brackets (e.g. <E1>, <E2>)
            string pattern = @"<([^>]+)>";

            // Use Regex.Replace with a match evaluator to replace each match
            string result = Regex.Replace(input, pattern, match =>
            {
                string key = match.Groups[1].Value;  // The key inside < > (e.g., "E1")
                return keyValuePairs.ContainsKey(key) ? keyValuePairs[key] : match.Value; // Replace with the dictionary value or keep the original if not found
            });

            return result;
        }

        public float GetDepthValue(string depthFile, string row, string col)
        {
            if (string.IsNullOrEmpty(depthFile))
            {
                return 1.0f;  // Default value
            }
            if (float.TryParse(depthFile, out float depth))
            {
                return depth;
            }
            int rowCount = 0;
            int colIndex = -1;
            row = row.ToUpper();  // Convert row to upper case for case-insensitive comparison
            col = col.ToUpper();  // Convert column to upper case for case-insensitive comparison
            if (!File.Exists(depthFile))
            {
                Console.WriteLine($"Error: File does not exist at path {depthFile}.");
                Environment.Exit(1);
            }
            foreach (string line in File.ReadLines(depthFile))
            {
                rowCount++;
                string[] parts = line.Split(',', '\t');

                if (rowCount == 1)
                {
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (parts[i].ToUpper() == col)  // Convert column to upper case for comparison
                        {
                            colIndex = i;
                        }
                    }
                }
                else
                {
                    if (parts[0].ToUpper() == row)  // Convert row to upper case for comparison
                    {
                        return float.Parse(parts[colIndex]);
                    }
                }
            }
            Console.WriteLine($"Could not find Depth in {depthFile} for {row}, {col}");
            Environment.Exit(0);
            return 0.0f;
        }
    }
}