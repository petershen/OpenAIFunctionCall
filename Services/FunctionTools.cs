using OpenAIFunctionCall.Extension;
using OpenAIFunctionCall.Interfaces;
using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OpenAIFunctionCall.Services
{
    internal class FunctionTools : IFunctionTools
    {
        private readonly WeatherApiHttpClient _weatherClient;
        private readonly DataApiHttpClient _dataClient;

        public FunctionTools(WeatherApiHttpClient weatherClient, DataApiHttpClient dataClient)
        {
            _weatherClient = weatherClient;
            _dataClient = dataClient;
        }

        public object[] Definition()
        {
            return [
                new
                {
                    type = "function",
                    function = new {
                        name = "get_current_weather",
                        description = "Get weather forecast for a given city and state location",
                        parameters = new {
                            type = "object",
                            properties = new {
                                    location = new {
                                        type = "string",
                                        description = "City and state, e.g., Columbia, Maryland"
                                    },
                                    unit = new {
                                        type = "string",
                                        description = "Fahrenheit or Celsius",
                                        Enum = new[] {"Celsius","Fahrenheit"}
                                    }
                                },
                            required = new[] { "location", "unit" }
                        },
                        tool_choice = "auto"
                    }
                },

                new
                {
                    type = "function",
                    function = new {
                        name = "get_weather_alerts",
                        description = "Get weather alerts for a US state code.",
                        parameters = new {
                            type = "object",
                            properties = new {
                                    state = new {
                                        type = "string",
                                        description = "The US state code to get alerts for."
                                    }
                                },
                            required = new[] { "state" }
                        },
                        tool_choice = "auto"
                    }
                },

                new
                {
                    type = "function",
                    function = new {
                        name = "get_weather_forcast",
                        description = "Get weather forecast for a given latitude and longitude location .",
                        parameters = new {
                            type = "object",
                            properties = new {
                                    latitude = new {
                                        type = "string",
                                        description = "Latitude of the location."
                                    },
                                    longitude = new {
                                        type = "string",
                                        description = "Longitude of the location."
                                    }
                                },
                            required = new[] { "latitude", "longitude" }
                        },
                        tool_choice = "auto"
                    }
                }
            ];
        }

        public Task<object> Implementation(string? toolName, string? argsJson)
        {
            switch (toolName)
            {
                case "get_weather_alerts":
                    if (string.IsNullOrWhiteSpace(argsJson))
                        throw new InvalidOperationException("Missing argument");

                    using (JsonDocument argsDoc = JsonDocument.Parse(argsJson))
                    {
                        string state = argsDoc.RootElement.GetProperty("state").GetString() ?? string.Empty;
                        return GetAlerts(_weatherClient, state);
                    }
                case "get_weather_forcast":
                    if (string.IsNullOrWhiteSpace(argsJson))
                        throw new InvalidOperationException("Missing argument");

                    using (JsonDocument argsDoc = JsonDocument.Parse(argsJson))
                    {
                        string? latitude = argsDoc.RootElement.GetProperty("latitude").GetString();
                        if (!double.TryParse(latitude, out double lati))
                            throw new InvalidOperationException("Invalid Latitude");
                        string? longitude = argsDoc.RootElement.GetProperty("longitude").GetString();
                        if (!double.TryParse(longitude, out double longi))
                            throw new InvalidOperationException("Invalid Longitude");
                        return GetForecast(_weatherClient, lati, longi);
                    }
                case "get_current_weather":
                    if (string.IsNullOrWhiteSpace(argsJson))
                        throw new InvalidOperationException("Missing argument");

                    using (JsonDocument argsDoc = JsonDocument.Parse(argsJson))
                    {
                        string location = argsDoc.RootElement.GetProperty("location").GetString() ?? string.Empty;
                        string unit = argsDoc.RootElement.GetProperty("unit").GetString() ?? "Fahrenheit";
                        return GetCurrentWeather(_dataClient, location, unit);
                    }
                default:
                    throw new InvalidOperationException("Unknown tool");
            }
        }

        //private Task<object> GetCurrentWeather(string location, string unit) => Task.FromResult<object>(
        //    new
        //    {
        //        location,
        //        temperature = 42,
        //        unit,
        //        condition = "Cloudy"
        //    }
        //);

        private async Task<object> GetCurrentWeather(DataApiHttpClient client, string location, string unit)
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36");
            string uri = $"https://nominatim.openstreetmap.org/search?q={location.Replace(' ', '+')}&format=json&limit=1";
            using var jsonDocument = await client.GetJsonDocumentAsync(uri);
            var jsonElement = jsonDocument.RootElement;
            var latitude = jsonElement.EnumerateArray().ElementAt(0).GetProperty("lat").GetString();
            var longitude = jsonElement.EnumerateArray().ElementAt(0).GetProperty("lon").GetString();

            if (!double.TryParse(latitude, out double lat))
                throw new InvalidOperationException("Invalid Latitude");
            if (!double.TryParse(longitude, out double lon))
                throw new InvalidOperationException("Invalid Longitude");

            return await GetForecast(_weatherClient, lat, lon);
        }

        private static async Task<object> GetAlerts(
            WeatherApiHttpClient client, string state)
        {
            using var jsonDocument = await client.GetJsonDocumentAsync($"/alerts/active/area/{state}");
            var jsonElement = jsonDocument.RootElement;
            var alerts = jsonElement.GetProperty("features").EnumerateArray();

            if (!alerts.Any())
            {
                return new { Event = "No active alerts for this state." };
            }

            List<object> alertsList = new List<object>();
            foreach (var alert in alerts)
            {
                JsonElement properties = alert.GetProperty("properties");
                alertsList.Add(new
                    {
                        Event = properties.GetProperty("event").GetString(),
                        Area = properties.GetProperty("areaDesc").GetString(),
                        Severity = properties.GetProperty("severity").GetString(),
                        Description = properties.GetProperty("description").GetString(),
                        Instruction = properties.GetProperty("instruction").GetString()
                    });
            }
            return alertsList.ToArray();
        }

        private static async Task<object> GetForecast(
            WeatherApiHttpClient client, double latitude, double longitude)
        {
            var pointUrl = string.Create(CultureInfo.InvariantCulture, $"/points/{latitude},{longitude}");
            using var jsonDocument = await client.GetJsonDocumentAsync(pointUrl);
            var forecastUrl = jsonDocument.RootElement.GetProperty("properties").GetProperty("forecast").GetString()
                ?? throw new Exception($"No forecast URL provided by {client.BaseAddress}points/{latitude},{longitude}");

            using var forecastDocument = await client.GetJsonDocumentAsync(forecastUrl);
            var periods = forecastDocument.RootElement.GetProperty("properties").GetProperty("periods").EnumerateArray();

            List<object> forecasts = new List<object>();
            foreach (var period in periods)
            {
                forecasts.Add(new
                {
                    Period = period.GetProperty("name").GetString(),
                    Temperature = $"{period.GetProperty("temperature").GetInt32()}°F",
                    Wind = period.GetProperty("windSpeed").GetString(),
                    Forecast = period.GetProperty("detailedForecast").GetString()
                });
            }
            return forecasts.ToArray();
        }

    }
}
