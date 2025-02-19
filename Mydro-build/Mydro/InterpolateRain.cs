using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace Mydro
{
    public class InterpolateRain
    {
        public InterpolateRain(string gaugeNetworkFile, DateTime startDate, DateTime endDate)
        {
            var results = new List<List<(DateTime, float)>>(); // (Timestamp, Intensity in mm/hr)

            using (var reader = new StreamReader(gaugeNetworkFile))
            using (var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true }))
            {
                var records = csv.GetRecords<NetworkEntry>();

                foreach (var record in records)
                {
                    if (!File.Exists(record.File))
                        continue; // Skip missing files

                    using (var fileReader = new StreamReader(record.File))
                    using (var fileCsv = new CsvReader(fileReader, new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = true }))
                    {
                        var rainRecords = fileCsv.GetRecords<RainEntry>();
                        DateTime? lastTimestamp = null;
                        float lastDepth = 0;

                        foreach (var rainRecord in rainRecords)
                        {
                            if (rainRecord.Date >= startDate && rainRecord.Date <= endDate)
                            {
                                if (lastTimestamp.HasValue)
                                {
                                    double hoursDiff = (rainRecord.Date - lastTimestamp.Value).TotalHours;
                                    if (hoursDiff > 0) // Avoid division by zero
                                    {
                                        float intensity = (rainRecord.Depth - lastDepth) / (float)hoursDiff;
                                        results.Add((rainRecord.Date, intensity));
                                    }
                                }
                                lastTimestamp = rainRecord.Date;
                                lastDepth = rainRecord.Depth;
                            }
                        }
                    }
                }
            }

            // Use results as needed
        }
    }

    public class NetworkEntry
    {
        public string File { get; set; }
        public float Lat { get; set; }
        public float Lon { get; set; }
        public bool Ignore { get; set; }
    }

    public class RainEntry
    {
        public DateTime Date { get; set; }
        public float Depth { get; set; }
    }
}