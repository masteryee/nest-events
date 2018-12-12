using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;

namespace Nest.Events.Listener
{
    class Program
    {
        public static IConfigurationRoot Configuration { get; set; }

        static HttpClient _httpClient;

        static Program()
        {
            var builder = new ConfigurationBuilder();
            builder.AddUserSecrets("ProductOAuth");
            Configuration = builder.Build();

            _httpClient = new HttpClient()
            {
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        static async Task Main(string[] args)
        {
            Console.WriteLine("Welcome to the Nest Event streamer!");
            await RunAsync().ConfigureAwait(false);
        }

        static async Task RunAsync()
        {
            var apiToken = await GetApiTokenAsync().ConfigureAwait(false);
            var streamReader = await OpenNestEventStreamAsync(apiToken);
            await DetectPersonAsync(streamReader).ConfigureAwait(false);
        }

        static async Task<string> GetApiTokenAsync()
        {
            var existingToken = await CheckForExistingTokenAsync();
            if (!string.IsNullOrEmpty(existingToken)) return existingToken;

            var nestAuthenticator = new NestAuthenticator(_httpClient,
                redirectUrl: "http://localhost:9999/",
                clientId: Configuration["Nest:ClientId"],
                clientSecret: Configuration["Nest:ClientSecret"]);

            var authorizationCode = await nestAuthenticator.ReceiveAuthorizationCodeAsync();
            return await nestAuthenticator.GetApiTokenAsync(authorizationCode).ConfigureAwait(false);
        }

        static async Task<string> CheckForExistingTokenAsync()
        {
            if (!File.Exists(NestAuthenticator.TokenCacheLocation)) return null;

            var json = await File.ReadAllTextAsync(NestAuthenticator.TokenCacheLocation);
            var existingToken = JsonConvert.DeserializeObject<NestAuthToken>(json);

            if (existingToken.Expiration < DateTime.UtcNow) return null;

            return existingToken.AccessToken;
        }

        static async Task<StreamReader> OpenNestEventStreamAsync(string apiToken)
        {
            // setup up streaming and authentication
            var request = new HttpRequestMessage(HttpMethod.Get, "https://developer-api.nest.com");
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            request.Headers.Add("Accept", "text/event-stream");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            var body = await response.Content.ReadAsStreamAsync();

            return new StreamReader(body);
        }

        static async Task DetectPersonAsync(StreamReader eventStreamReader)
        {
            var notifier = new AwsSmsSender(Configuration["Aws:AccessKeyId"],
                Configuration["Aws:SecretAccessKey"],
                Configuration["Aws:RegionEndpoint"],
                Configuration["Aws:SnsTopicArn"]);

            var lastEvents = new Dictionary<string, string>();
            bool isExpectingDeviceData = false;
            bool isFirstTime = true;

            while (!eventStreamReader.EndOfStream)
            {
                // We are ready to read the stream
                var currentLine = await eventStreamReader.ReadLineAsync();
                if (currentLine == "event: put")
                {
                    isExpectingDeviceData = true;
                    continue;
                }

                if (isExpectingDeviceData && currentLine.StartsWith("data: ", StringComparison.Ordinal))
                {
                    var json = currentLine.Substring(6);
                    var jobject = JObject.Parse(json);
                    var data = jobject["data"];
                    var devices = data["devices"];
                    var cameras = devices["cameras"];
                    if (cameras == null) continue;

                    foreach (var cameraJson in cameras.Children())
                    {
                        var myCamera = cameraJson.First;
                        var lastEvent = myCamera["last_event"];
                        if (lastEvent == null) continue;

                        var detectedPerson = (bool)lastEvent["has_person"];
                        var activityZoneIds = lastEvent["activity_zone_ids"]?.Values<string>().ToArray();
                        if (detectedPerson)
                        {
                            var deviceName = myCamera["name"].ToString();
                            var startTime = lastEvent["start_time"].ToString();
                            var endTime = (lastEvent["end_time"] ?? "N/A").ToString();
                            Console.WriteLine($"{GetLocalTime(DateTime.UtcNow.ToString())}: {deviceName}: {GetLocalTime(startTime)} to {GetLocalTime(endTime)}");

                            // ignore the event when an end date is written
                            if (lastEvents.TryGetValue(deviceName, out string lastEventDate))
                            {
                                if (lastEventDate == startTime)
                                {
                                    continue;
                                }
                            }

                            lastEvents[deviceName] = startTime;

                            // no alerts on the first data
                            var timeRange = DateTime.Parse(startTime).ToLocalTime().TimeOfDay;
                            if (isFirstTime || timeRange.Hours == 7 || timeRange.Hours == 8 || (timeRange.Hours == 9 && timeRange.Minutes < 30))
                            {
                                isFirstTime = false;
                                continue;
                            }

                            // no alerts if activity zones are set and person was not in an activity zone
                            if (activityZoneIds != null && activityZoneIds.Length == 0)
                            {
                                continue;
                            }

                            await notifier.SendNotificationAsync(deviceName, GetLocalTime(startTime)).ConfigureAwait(false);
                        }
                    }

                    isExpectingDeviceData = false;
                }
            }
        }

        private static string GetLocalTime(string utcTimeString)
        {
            if (!DateTime.TryParse(utcTimeString, out DateTime utcTime))
            {
                return utcTimeString;
            }

            return utcTime.ToLocalTime().ToString("yyyy-MM-dd h:mm:sstt");
        }
    }
}
