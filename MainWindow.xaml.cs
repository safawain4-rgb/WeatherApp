using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace weatherForecasting
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private static readonly HttpClient Http = new HttpClient();
        private bool _useFahrenheit;
        private double _lastTempC;
        private double _lastFeelsLikeC;

        public MainWindow()
        {
            InitializeComponent();
        }

        // ===================================================================
        // Search box placeholder behaviour
        // ===================================================================
        private void CityTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            SearchPlaceholder.Visibility = Visibility.Collapsed;
        }

        private void CityTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CityTextBox.Text))
                SearchPlaceholder.Visibility = Visibility.Visible;
        }

        private void CityTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(CityTextBox.Text) && !CityTextBox.IsFocused
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private async void CityTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                await SearchAsync();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await SearchAsync();
        }

        private void UnitToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (sender == CelsiusToggle)
            {
                _useFahrenheit = false;
                FahrenheitToggle.IsChecked = false;
            }
            else
            {
                _useFahrenheit = true;
                CelsiusToggle.IsChecked = false;
            }

            if (CurrentWeatherCard.Visibility == Visibility.Visible)
                RefreshTemperatureDisplay();
        }

        // ===================================================================
        // Core search flow
        // ===================================================================
        private async Task SearchAsync()
        {
            string city = CityTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(city))
            {
                ShowError("Please type a city name first.");
                return;
            }

            ShowLoading();

            try
            {
                GeocodingResult location = await GeocodeCityAsync(city);
                if (location == null)
                {
                    ShowError($"No results found for \"{city}\". Try a different spelling.");
                    return;
                }

                ForecastResponse forecast = await GetForecastAsync(location.Latitude, location.Longitude);
                if (forecast?.Current == null || forecast.Daily == null)
                {
                    ShowError("Weather data is unavailable right now. Please try again.");
                    return;
                }

                DisplayWeather(location, forecast);
            }
            catch (HttpRequestException)
            {
                ShowError("Couldn't reach the weather service. Check your internet connection.");
            }
            catch (TaskCanceledException)
            {
                ShowError("The request timed out. Please try again.");
            }
            catch (Exception ex)
            {
                ShowError("Something went wrong: " + ex.Message);
            }
        }

        private async Task<GeocodingResult> GeocodeCityAsync(string city)
        {
            string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&language=en&format=json&name="
                         + Uri.EscapeDataString(city);

            using (HttpResponseMessage response = await Http.GetAsync(url))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                GeocodingResponse parsed = DeserializeJson<GeocodingResponse>(json);

                if (parsed?.Results == null || parsed.Results.Count == 0)
                    return null;

                return parsed.Results[0];
            }
        }

        private async Task<ForecastResponse> GetForecastAsync(double latitude, double longitude)
        {
            string lat = latitude.ToString(CultureInfo.InvariantCulture);
            string lon = longitude.ToString(CultureInfo.InvariantCulture);

            string url = "https://api.open-meteo.com/v1/forecast" +
                         $"?latitude={lat}&longitude={lon}" +
                         "&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m" +
                         "&daily=weather_code,temperature_2m_max,temperature_2m_min" +
                         "&timezone=auto&forecast_days=6";

            using (HttpResponseMessage response = await Http.GetAsync(url))
            {
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                return DeserializeJson<ForecastResponse>(json);
            }
        }

        private static T DeserializeJson<T>(string json)
        {
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(stream);
            }
        }

        // ===================================================================
        // UI states
        // ===================================================================
        private void ShowLoading()
        {
            ErrorBanner.Visibility = Visibility.Collapsed;
            IdlePanel.Visibility = Visibility.Collapsed;
            CurrentWeatherCard.Visibility = Visibility.Collapsed;
            ForecastLabel.Visibility = Visibility.Collapsed;
            ForecastScroll.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Visible;
            SearchButton.IsEnabled = false;
        }

        private void ShowError(string message)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            SearchButton.IsEnabled = true;
            ErrorText.Text = message;
            ErrorBanner.Visibility = Visibility.Visible;

            if (CurrentWeatherCard.Visibility != Visibility.Visible)
                IdlePanel.Visibility = Visibility.Visible;
        }

        private void DisplayWeather(GeocodingResult location, ForecastResponse forecast)
        {
            SearchButton.IsEnabled = true;
            ErrorBanner.Visibility = Visibility.Collapsed;
            IdlePanel.Visibility = Visibility.Collapsed;
            LoadingPanel.Visibility = Visibility.Collapsed;

            // --- Location & date ---
            string region = !string.IsNullOrEmpty(location.Admin1) ? $", {location.Admin1}" : "";
            CityNameText.Text = $"{location.Name}{region}";
            DateText.Text = DateTime.Now.ToString("dddd, MMMM d", CultureInfo.InvariantCulture);

            // --- Current conditions ---
            CurrentWeather current = forecast.Current;
            _lastTempC = current.Temperature;
            _lastFeelsLikeC = current.ApparentTemperature;

            WeatherIconText.Text = GetWeatherIcon(current.WeatherCode);
            ConditionText.Text = GetWeatherDescription(current.WeatherCode);
            HumidityText.Text = $"{Math.Round(current.Humidity)}%";
            WindText.Text = $"{Math.Round(current.WindSpeed)} km/h";

            RefreshTemperatureDisplay();

            CurrentWeatherCard.Visibility = Visibility.Visible;

            // --- Daily forecast strip (skip today, show next 5 days) ---
            var items = new ObservableCollection<DayForecastItem>();
            DailyForecast daily = forecast.Daily;

            int count = daily.Time?.Count ?? 0;
            for (int i = 1; i < count && items.Count < 5; i++)
            {
                DateTime date;
                DateTime.TryParse(daily.Time[i], CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

                items.Add(new DayForecastItem
                {
                    Day = date != default(DateTime) ? date.ToString("ddd", CultureInfo.InvariantCulture) : $"Day {i}",
                    Icon = GetWeatherIcon(daily.WeatherCode[i]),
                    TempMax = FormatTemp(daily.TempMax[i]),
                    TempMin = FormatTemp(daily.TempMin[i])
                });
            }

            ForecastItemsControl.ItemsSource = items;
            ForecastLabel.Visibility = Visibility.Visible;
            ForecastScroll.Visibility = Visibility.Visible;
        }

        private void RefreshTemperatureDisplay()
        {
            TemperatureText.Text = FormatTemp(_lastTempC);
            FeelsLikeText.Text = $"Feels like {FormatTemp(_lastFeelsLikeC)}";
        }

        private string FormatTemp(double celsius)
        {
            double value = _useFahrenheit ? celsius * 9.0 / 5.0 + 32.0 : celsius;
            string unit = _useFahrenheit ? "°F" : "°C";
            return $"{Math.Round(value)}{unit}";
        }

        // ===================================================================
        // WMO weather-code mapping
        // ===================================================================
        private static string GetWeatherIcon(int code)
        {
            switch (code)
            {
                case 0: return "☀️";
                case 1:
                case 2: return "🌤️";
                case 3: return "☁️";
                case 45:
                case 48: return "🌫️";
                case 51:
                case 53:
                case 55: return "🌦️";
                case 56:
                case 57: return "🌧️";
                case 61:
                case 63:
                case 65: return "🌧️";
                case 66:
                case 67: return "🌧️";
                case 71:
                case 73:
                case 75:
                case 77: return "❄️";
                case 80:
                case 81:
                case 82: return "🌧️";
                case 85:
                case 86: return "🌨️";
                case 95:
                case 96:
                case 99: return "⛈️";
                default: return "🌡️";
            }
        }

        private static string GetWeatherDescription(int code)
        {
            switch (code)
            {
                case 0: return "Clear sky";
                case 1: return "Mainly clear";
                case 2: return "Partly cloudy";
                case 3: return "Overcast";
                case 45: return "Fog";
                case 48: return "Freezing fog";
                case 51: return "Light drizzle";
                case 53: return "Drizzle";
                case 55: return "Dense drizzle";
                case 56:
                case 57: return "Freezing drizzle";
                case 61: return "Light rain";
                case 63: return "Rain";
                case 65: return "Heavy rain";
                case 66:
                case 67: return "Freezing rain";
                case 71: return "Light snow";
                case 73: return "Snow";
                case 75: return "Heavy snow";
                case 77: return "Snow grains";
                case 80: return "Light rain showers";
                case 81: return "Rain showers";
                case 82: return "Violent rain showers";
                case 85: return "Snow showers";
                case 86: return "Heavy snow showers";
                case 95: return "Thunderstorm";
                case 96:
                case 99: return "Thunderstorm with hail";
                default: return "Unknown";
            }
        }
    }

    // ===========================================================================
    // View model for forecast strip items
    // ===========================================================================
    public class DayForecastItem
    {
        public string Day { get; set; }
        public string Icon { get; set; }
        public string TempMax { get; set; }
        public string TempMin { get; set; }
    }

    // ===========================================================================
    // DTOs matching the Open-Meteo JSON responses
    // ===========================================================================
    [DataContract]
    public class GeocodingResponse
    {
        [DataMember(Name = "results")]
        public System.Collections.Generic.List<GeocodingResult> Results { get; set; }
    }

    [DataContract]
    public class GeocodingResult
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "latitude")]
        public double Latitude { get; set; }

        [DataMember(Name = "longitude")]
        public double Longitude { get; set; }

        [DataMember(Name = "country")]
        public string Country { get; set; }

        [DataMember(Name = "admin1")]
        public string Admin1 { get; set; }
    }

    [DataContract]
    public class ForecastResponse
    {
        [DataMember(Name = "current")]
        public CurrentWeather Current { get; set; }

        [DataMember(Name = "daily")]
        public DailyForecast Daily { get; set; }

        [DataMember(Name = "timezone")]
        public string Timezone { get; set; }
    }

    [DataContract]
    public class CurrentWeather
    {
        [DataMember(Name = "temperature_2m")]
        public double Temperature { get; set; }

        [DataMember(Name = "relative_humidity_2m")]
        public double Humidity { get; set; }

        [DataMember(Name = "apparent_temperature")]
        public double ApparentTemperature { get; set; }

        [DataMember(Name = "weather_code")]
        public int WeatherCode { get; set; }

        [DataMember(Name = "wind_speed_10m")]
        public double WindSpeed { get; set; }

        [DataMember(Name = "time")]
        public string Time { get; set; }
    }

    [DataContract]
    public class DailyForecast
    {
        [DataMember(Name = "time")]
        public System.Collections.Generic.List<string> Time { get; set; }

        [DataMember(Name = "weather_code")]
        public System.Collections.Generic.List<int> WeatherCode { get; set; }

        [DataMember(Name = "temperature_2m_max")]
        public System.Collections.Generic.List<double> TempMax { get; set; }

        [DataMember(Name = "temperature_2m_min")]
        public System.Collections.Generic.List<double> TempMin { get; set; }
    }
}
