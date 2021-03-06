﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Nest.Events.Listener
{
    public class PersonDetector : INestEventConsumer
    {
        private const string NestApiEndpoint = "https://developer-api.nest.com";

        private readonly HttpClient _httpClient;
        private readonly NestAuthenticator _nestAuthenticator;
        private readonly Func<string, INestEventNotifier> _notifierFactory;

        public PersonDetector(HttpClient httpClient,
            NestAuthenticator nestAuthenticator,
            Func<string, INestEventNotifier> notifierFactory)
        {
            _httpClient = httpClient;
            _nestAuthenticator = nestAuthenticator;
            _notifierFactory = notifierFactory;
        }

        public async Task ListenAsync()
        {
            Console.WriteLine($"{GetLocalTime(DateTime.UtcNow.ToString())}: Get API token...");

            var apiToken = await GetApiTokenAsync().ConfigureAwait(false);
            while (true)
            {
                Console.WriteLine($"{GetLocalTime(DateTime.UtcNow.ToString())}: Open event stream...");

                var streamReader = await OpenNestEventStreamAsync(apiToken);
                try
                {
                    Console.WriteLine($"{GetLocalTime(DateTime.UtcNow.ToString())}: Begin listening for person events...");

                    await DetectPersonAsync(streamReader).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    Console.WriteLine(ex.StackTrace);
                }
                finally
                {
                    Console.WriteLine($"{GetLocalTime(DateTime.UtcNow.ToString())}: Retrying in 5 seconds...");
                    var task = Task.Delay(5000);
                    await task.ConfigureAwait(false);
                    task.Dispose();
                }
            }
        }

        async Task<string> GetApiTokenAsync()
        {
            var existingToken = await _nestAuthenticator.CheckForExistingTokenAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(existingToken)) return existingToken;

            var authorizationCode = await _nestAuthenticator.ReceiveAuthorizationCodeAsync().ConfigureAwait(false);
            return await _nestAuthenticator.GetApiTokenAsync(authorizationCode).ConfigureAwait(false);
        }

        async Task<StreamReader> OpenNestEventStreamAsync(string apiToken)
        {
            // setup up streaming and authentication
            var request = new HttpRequestMessage(HttpMethod.Get, NestApiEndpoint);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            request.Headers.Add("Accept", "text/event-stream");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);

            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            var body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            return new StreamReader(body);
        }

        async Task DetectPersonAsync(StreamReader eventStreamReader)
        {
            var notifier = _notifierFactory("aws");

            var lastEvents = new Dictionary<string, string>();
            bool isExpectingDeviceData = false;
            bool isFirstTime = true;

            while (!eventStreamReader.EndOfStream)
            {
                // We are ready to read the stream
                var currentLine = await eventStreamReader.ReadLineAsync().ConfigureAwait(false);
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
                        var deviceName = myCamera["name"].ToString();
                        var startTime = lastEvent["start_time"].ToString();

                        var hasPerson = (bool)lastEvent["has_person"];
                        var hasMotion = (bool)lastEvent["has_motion"];
                        var hasSound = (bool)lastEvent["has_sound"];
                        if (hasPerson)
                        {
                            Console.WriteLine($"{GetLocalTime(DateTime.UtcNow.ToString())}: {deviceName} ### PERSON ### @ {GetLocalTime(startTime)}");
                        }
                        else if (hasMotion)
                        {
                            Console.WriteLine($"{GetLocalTime(DateTime.UtcNow.ToString())}: {deviceName} Motion @ {GetLocalTime(startTime)}");
                        }
                        else if (hasSound)
                        {
                            Console.WriteLine($"{GetLocalTime(DateTime.UtcNow.ToString())}: {deviceName} Sound @ {GetLocalTime(startTime)}");
                        }

                        if (hasPerson)
                        {
                            // no alerts if activity zones are set and person was not in an activity zone
                            var activityZoneIds = lastEvent["activity_zone_ids"]?.Values<string>().ToArray();
                            if (activityZoneIds == null || activityZoneIds.Length == 0)
                            {
                                Console.WriteLine($"Person not in any activity zone: {lastEvent}");
                                continue;
                            }

                            // accept only new detections
                            if (lastEvents.TryGetValue(deviceName, out string lastEventDate))
                            {
                                if (lastEventDate == startTime)
                                {
                                    continue;
                                }

                                // do not send another notification for a new event within 1 minute
                                if (DateTime.Parse(lastEventDate).AddMinutes(1).CompareTo(DateTime.Parse(startTime)) == 1)
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

                            // no alerts between 7AM and 9:30AM
                            var timeRange = DateTime.Parse(startTime).ToLocalTime().TimeOfDay;
                            if (timeRange.Hours == 7 || timeRange.Hours == 8 || (timeRange.Hours == 9 && timeRange.Minutes < 30))
                            {
                                continue;
                            }

                            Console.WriteLine($"{GetLocalTime(DateTime.UtcNow.ToString())}: {deviceName} detected person @ {GetLocalTime(startTime)} in zone(s) {string.Join(',', activityZoneIds)}");

                            await notifier.SendNotificationAsync(deviceName, GetLocalTime(startTime)).ConfigureAwait(false);
                        }
                    }

                    isFirstTime = false;
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
