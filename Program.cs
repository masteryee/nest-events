﻿using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;

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

        static void Main(string[] args)
        {
            Console.WriteLine("Welcome to the Nest Event streamer!");
            var task = Task.Run(async () => await RunAsync().ConfigureAwait(false));
            task.Wait();
        }

        static async Task RunAsync()
        {
            var apiToken = await GetApiTokenAsync();
            var streamReader = await OpenNestEventStreamAsync(apiToken);
            await DetectPersonAsync(streamReader);
        }

        static async Task<string> GetApiTokenAsync()
        {
            var nestAuthenticator = new NestAuthenticator(_httpClient,
                redirectUrl: "http://localhost:9999/",
                clientId: Configuration["Nest:ClientId"],
                clientSecret: Configuration["Nest:ClientSecret"]);

            var authorizationCode = await nestAuthenticator.ReceiveAuthorizationCodeAsync();
            return await nestAuthenticator.GetApiTokenAsync(authorizationCode);
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
                HttpCompletionOption.ResponseHeadersRead);
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
                            if (isFirstTime)
                            {
                                isFirstTime = false;
                                continue;
                            }

                            await notifier.SendNotificationAsync(deviceName, GetLocalTime(startTime));
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
