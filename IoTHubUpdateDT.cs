using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using Azure;
using Azure.Core.Pipeline;
using Azure.DigitalTwins.Core;
using Azure.Identity;
using Microsoft.Azure.EventGrid.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

//https://docs.microsoft.com/en-us/azure/digital-twins/how-to-ingest-iot-hub-data

namespace dtUpdater {

    public class Properties {
        [JsonProperty ("@type")]
        public string Type { get; set; }
        public double personCount { get; set; }
        public Int32 frameNumber { get; set; }

        public string frameDate { get; set; }
    }

    public class Event {
        public string id { get; set; }
        public string type { get; set; }
        public List<string> detectionIds { get; set; }
        public Properties properties { get; set; }
        public string zone { get; set; }
        public string trigger { get; set; }
    }

    public class CameraCalibrationInfo {
        public string status { get; set; }
        public double cameraHeight { get; set; }
        public double focalLength { get; set; }
        public double tiltupAngle { get; set; }
        public DateTime lastCalibratedTime { get; set; }
    }

    public class SourceInfo {
        public string id { get; set; }
        public DateTime timestamp { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public string frameId { get; set; }
        public CameraCalibrationInfo cameraCalibrationInfo { get; set; }
        public string imagePath { get; set; }
    }

    public class Point {
        public double x { get; set; }
        public double y { get; set; }
    }

    public class Region {
        public string type { get; set; }
        public List<Point> points { get; set; }
        public string name { get; set; }
    }

    public class Attributes { }

    public class Metadata {
        public string trackingId { get; set; }
        public double groundOrientationAngle { get; set; }
        public double mappedImageOrientation { get; set; }
        public string speed { get; set; }
        public Attributes attributes { get; set; }
    }

    public class CenterGroundPoint {
        public double x { get; set; }
        public double y { get; set; }
    }

    public class Footprint {
        public double x { get; set; }
        public double y { get; set; }
    }

    public class Detection {
        public string type { get; set; }
        public string metadataType { get; set; }
        public string id { get; set; }
        public Region region { get; set; }
        public Metadata metadata { get; set; }
        public double confidence { get; set; }
        public CenterGroundPoint centerGroundPoint { get; set; }
        public Footprint footprint { get; set; }
    }

    public class Root {
        public List<Event> events { get; set; }
        public SourceInfo sourceInfo { get; set; }
        public List<Detection> detections { get; set; }
        public string schemaVersion { get; set; }
    }

    public class dtUpdater {

        private static string adtAppId = "https://digitaltwins.azure.net";
        private static readonly HttpClient httpClient = new HttpClient ();
        private static readonly string adtInstanceUrl = Environment.GetEnvironmentVariable ("ADT_SERVICE_URL");

        [FunctionName ("IoTHubtoTwinsUpdater")]
        public void Run ([EventGridTrigger] EventGridEvent eventGridEvent, ILogger log) {

            JObject deviceMessage = (JObject) JsonConvert.DeserializeObject (eventGridEvent.Data.ToString ());

            JObject deviceMessage1 = (JObject) JsonConvert.DeserializeObject (Encoding.UTF8.GetString (Convert.FromBase64String (deviceMessage["body"].ToString ())));

            Root myDeserializedClass = JsonConvert.DeserializeObject<Root> (deviceMessage1.ToString ());

            log.LogInformation ($"{deviceMessage1.ToString()}");

            int occupancy = (int) myDeserializedClass.events[0].properties.personCount;
            int fid = Convert.ToInt32 (myDeserializedClass.sourceInfo.frameId);
            string date = myDeserializedClass.sourceInfo.timestamp.ToString ();

            if (occupancy > 0) {
                log.LogInformation ($"-----------{occupancy}-------------------------{fid}--------------{date}---------------\n");

                ManagedIdentityCredential cred = new ManagedIdentityCredential (adtAppId);
                DigitalTwinsClientOptions opts = new DigitalTwinsClientOptions { Transport = new HttpClientTransport (httpClient) };
                DigitalTwinsClient client = new DigitalTwinsClient (new Uri (adtInstanceUrl), cred, opts);

                //  DeleteTwins(client, log);

                var twinAreaData = new BasicDigitalTwin ();
                twinAreaData.Metadata.ModelId = "dtmi:adt:area;1";
                twinAreaData.Contents.Add ("personCount", occupancy);
                twinAreaData.Contents.Add ("frameNumber", fid);
                twinAreaData.Contents.Add ("frameDate", date);

                twinAreaData.Id = $"{myDeserializedClass.sourceInfo.id}";

                List<Detection> detection = myDeserializedClass.detections;
                List<Footprint> f = new List<Footprint> ();
                foreach (Detection d in detection) {

                    f.Add (d.footprint);
                }
                string jObk = JsonConvert.SerializeObject (f);
                log.LogInformation ($"--------------{jObk}-----------------");
                twinAreaData.Contents.Add ("Persons", jObk);

                CreateTwin (client, twinAreaData, log);

            }
        }

        public static void CreateTwin (DigitalTwinsClient client, BasicDigitalTwin twinData, ILogger log) {
            try {
                var x = client.CreateOrReplaceDigitalTwin<BasicDigitalTwin> (twinData.Id, twinData);

                log.LogInformation ($">>>>>>Created  digital twin  '{x.Value.Id}' ");
            } catch (Azure.RequestFailedException e) {
                log.LogInformation ($"-----------------Delete twin error: {e.Status}: {e.Message}");
            }

        }
        public static void DeleteTwins (DigitalTwinsClient client, ILogger log) {

            try {
                string query = "SELECT * FROM digitaltwins";
                Azure.Pageable<BasicDigitalTwin> queryResult = client.Query<BasicDigitalTwin> (query);

                foreach (BasicDigitalTwin twin in queryResult) {

                    Azure.Pageable<BasicRelationship> rels = client.GetRelationships<BasicRelationship> (twin.Id);
                    foreach (BasicRelationship rel in rels) {
                        client.DeleteRelationship (twin.Id, rel.Id);
                        log.LogInformation ($"<<<<<<<<<<<<<<Found and deleted relationship '{rel.Id}'.");
                    }

                    client.DeleteDigitalTwinAsync (twin.Id);
                    log.LogInformation ($"<<<<<<<<<<<<<<<Deleted digital twin '{twin.Id}'.");

                }

            } catch (Azure.RequestFailedException e) {
                log.LogInformation ($"-----------------Delete relations error: {e.Status}: {e.Message}");
            }

        }

        private static void CreateRelationship (DigitalTwinsClient client, string srcId, string targetId, string relName, IDictionary<string, object> inputProperties, ILogger log) {
            var relationship = new BasicRelationship {
                TargetId = targetId,
                Name = relName,
                Properties = inputProperties
            };
            try {
                string relId = $"{srcId}-{relName}->{targetId}";
                Azure.Response<BasicRelationship> createBuildingFloorRelationshipResponse = client.CreateOrReplaceRelationship<BasicRelationship> (srcId, relId, relationship);

                log.LogInformation ($">>>>>>Created  digital twin relationship '{createBuildingFloorRelationshipResponse.Value.Id}' " +
                    $"from twin '{createBuildingFloorRelationshipResponse.Value.SourceId}' to twin '{createBuildingFloorRelationshipResponse.Value.TargetId}'.");
            } catch (Azure.RequestFailedException rex) {
                log.LogInformation ($"---------------Create relationship error: {rex.Status}:{rex.Message}");
            }

        }

    }

}