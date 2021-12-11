using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Core.Pipeline;
using Azure.DigitalTwins.Core;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace dtReader {
  public static class ReadAzureDT {

    public class Footprint {
      public double x { get; set; }
      public double y { get; set; }
    }

    public class Area {
      public int frameNumber { get; set; }
      public double personCount { get; set; }
      public string frameDate { get; set; }
      public string Persons { get; set; }
      public List<Footprint> footprint { get; set; }

    }
    private static string adtAppId = "https://digitaltwins.azure.net";
    private static readonly HttpClient httpClient = new HttpClient ();
    private static readonly string adtInstanceUrl = Environment.GetEnvironmentVariable ("ADT_SERVICE_URL");

    [FunctionName ("ReadAzureDT")]
    public static async Task<IActionResult> Run (
      [HttpTrigger (AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
      ILogger log) {

      ManagedIdentityCredential cred = new ManagedIdentityCredential (adtAppId);
      DigitalTwinsClientOptions opts = new DigitalTwinsClientOptions { Transport = new HttpClientTransport (httpClient) };
      DigitalTwinsClient client = new DigitalTwinsClient (new Uri (adtInstanceUrl), cred, opts);

      Area area = new Area ();
      var result = client.GetDigitalTwin<Area> ("CheckoutArea");

      return new OkObjectResult (result.Value);
    }

  }

}