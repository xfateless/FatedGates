using ProjectM;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace FatedGates.Services
{
    internal class LocalizationService
    {
        Dictionary<string, string> localization = [];

        public LocalizationService()
        {
            LoadLocalization();
        }

        void LoadLocalization()
        {
            var resourceName = "FatedGates.Localization.English.json";

            var assembly = Assembly.GetExecutingAssembly();
            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using (var reader = new StreamReader(stream))
                {
                    string jsonContent = reader.ReadToEnd();
                    localization = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent);
                }
            }
            else
            {
                Console.WriteLine("Resource not found!");
            }
        }

        public string GetLocalization(string guid)
        {
            if (localization.TryGetValue(guid, out var text))
            {
                return text;
            }
            return "<Localization not found!>";
        }
    }
}
