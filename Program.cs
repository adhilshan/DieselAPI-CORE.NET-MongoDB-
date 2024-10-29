using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;

public class DieselPrice
{
    public ObjectId Id { get; set; }
    public string State { get; set; }
    public string Price { get; set; }
    public string Change { get; set; }
    public string ChangeStatus { get; set; }
    public string Date { get; set; }
}

public class MongoDbService
{
    private readonly IMongoCollection<DieselPrice> _allStatesCollection;
    private readonly IMongoCollection<DieselPrice> _cityCollection;
    private readonly IMongoCollection<DieselPrice> _stateCollection;

    public MongoDbService(IMongoClient mongoClient)
    {
        var database = mongoClient.GetDatabase("FR24");
        _allStatesCollection = database.GetCollection<DieselPrice>("DieselAllStates");
        _cityCollection = database.GetCollection<DieselPrice>("DieselByCities");
        _stateCollection = database.GetCollection<DieselPrice>("DieselByState");
    }

    public async Task SaveAllStatesDataAsync(List<DieselPrice> data)
    {
        await _allStatesCollection.InsertManyAsync(data);
    }

    public async Task SaveCityDataAsync(List<DieselPrice> data)
    {
        await _cityCollection.InsertManyAsync(data);
    }

    public async Task SaveStateDataAsync(List<DieselPrice> data)
    {
        await _stateCollection.InsertManyAsync(data);
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        host.Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
            });
}

public class Startup
{
    public IConfiguration Configuration { get; }

    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers().AddNewtonsoftJson();
        services.AddSingleton<IMongoClient>(s =>
        {
            var client = new MongoClient(Configuration.GetConnectionString("MongoDefault"));
            return client;
        });

        services.AddSingleton<MongoDbService>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }
}

[ApiController]
[Route("api/[controller]")]
public class DieselPriceController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;
    private static readonly HttpClient HttpClient = new HttpClient();

    public DieselPriceController(MongoDbService mongoDbService)
    {
        _mongoDbService = mongoDbService;
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAllData()
    {
        string url = "https://www.ndtv.com/fuel-prices/diesel-price-in-all-state";
        var scrapedData = await ScrapeData(url);
        var dieselPrices = scrapedData.Select(item => new DieselPrice
        {
            State = item.State,
            Price = item.Price,
            Change = item.Change,
            ChangeStatus = item.ChangeStatus,
            Date = DateTime.Now.ToString("dd/MM/yy")
        }).ToList();

        await _mongoDbService.SaveAllStatesDataAsync(dieselPrices);
        return Ok(dieselPrices);
    }

    [HttpGet("bycity/recent")]
    public async Task<IActionResult> GetCityWise([FromQuery] string city)
    {
        string cityUrlFriendly = city.Replace(" ", "-").ToLower();
        string url = $"https://www.ndtv.com/fuel-prices/diesel-price-in-{cityUrlFriendly}-city";
        var scrapedData = await ScrapeData(url);
        var dieselPrices = scrapedData.Take(10).Select(item => new DieselPrice
        {
            State = city,
            Price = item.Price,
            Change = item.Change,
            ChangeStatus = item.ChangeStatus,
            Date = DateTime.Now.ToString("dd/MM/yy")
        }).ToList();

        await _mongoDbService.SaveCityDataAsync(dieselPrices);
        return Ok(dieselPrices);
    }

    [HttpGet("bystate/recent")]
    public async Task<IActionResult> GetStateWise([FromQuery] string state)
    {
        string url = $"https://www.ndtv.com/fuel-prices/diesel-price-in-{state}-state";
        var scrapedData = await ScrapeData(url);
        var dieselPrices = scrapedData.Skip(10).Take(10).Select(item => new DieselPrice
        {
            State = state,
            Price = item.Price,
            Change = item.Change,
            ChangeStatus = item.ChangeStatus,
            Date = DateTime.Now.ToString("dd/MM/yy")
        }).ToList();

        await _mongoDbService.SaveStateDataAsync(dieselPrices);
        return Ok(dieselPrices);
    }

    private async Task<List<DieselPrice>> ScrapeData(string url)
    {
        var response = await HttpClient.GetAsync(url);
        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            var doc = new HtmlDocument();
            doc.LoadHtml(content);
            var rows = doc.DocumentNode.SelectNodes("//tr");
            var data = new List<DieselPrice>();

            if (rows != null)
            {
                foreach (var row in rows.Skip(1)) // Skip the header row
                {
                    var cells = row.SelectNodes("td");
                    if (cells != null && cells.Count == 3)
                    {
                        string state = cells[0].InnerText.Trim();
                        string price = cells[1].InnerText.Trim();
                        string change = cells[2].InnerText.Trim();
                        string changeStatus = GetChangeStatus(cells[2]);

                        data.Add(new DieselPrice
                        {
                            State = state,
                            Price = price,
                            Change = change,
                            ChangeStatus = changeStatus
                        });
                    }
                }
            }
            return data;
        }
        else
        {
            throw new Exception($"Failed to retrieve the webpage. Status code: {response.StatusCode}");
        }
    }

    private string GetChangeStatus(HtmlNode node)
    {
        if (node.InnerText.Contains("-"))
        {
            return "Decrease";
        }
        else if (node.InnerText.Contains("+"))
        {
            return "Increase";
        }
        else
        {
            return "No Change";
        }
    }
}
