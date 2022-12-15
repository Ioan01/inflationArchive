using System.Web;
using InflationArchive.Helpers;
using InflationArchive.Models.Products;
using Newtonsoft.Json.Linq;

namespace InflationArchive.Services;

public class MetroScraper : AbstractStoreScraper
{
    // ids of the products
    private const string BaseIdUrl = "https://produse.metro.ro/explore.articlesearch.v1/search?storeId=00013&language=ro-RO&country=RO&query=*&rows=1000&page=1&filter=%FILTER%";

    // data of the products queried
    private const string BaseDataUrl = "https://produse.metro.ro/evaluate.article.v1/betty-variants?storeIds=00013&country=RO&locale=ro-RO";


    public MetroScraper(HttpClient httpClient, ProductService productService) : base(httpClient, productService)
    {
    }

    protected override string StoreName => "Metro";

    private static List<string> GenerateDataUrls(List<JToken> idList)
    {
        var dataRequestUrls = new List<string>();
        var dataUrl = BaseDataUrl;


        for (var i = 1; i <= idList.Count; i++)
        {
            dataUrl += "&ids=" + idList[i - 1].Value<string>();
            if (i % 40 == 0)
            {
                dataRequestUrls.Add(dataUrl);

                dataUrl = BaseDataUrl;
            }
        }

        dataRequestUrls.Add(dataUrl);

        return dataRequestUrls;
    }

    private static double ExtractPrice(JToken item)
    {
        var priceInfo = item["stores"]!.First!.First!["sellingPriceInfo"]!;

        return priceInfo["finalPrice"]!.Value<double>();
    }

    private async Task<Product?> TryExtractProduct(JToken item, int categoryId)
    {
        item = item.First!;
        var outerData = item["variants"]!.First!.First!;


        var imageUrl = outerData["imageUrl"]!.Value<string>();
        var name = outerData["description"]!.Value<string>()!;


        var innerData = outerData["bundles"]!.First!.First!;

        var description = innerData["description"]!.Value<string>();
        if (description is null)
            return null;

        var manufacturer = innerData["brandName"]!.Value<string>()?.OnlyFirstCharToUpper();
        if (manufacturer is null)
            return null;

        var price = ExtractPrice(innerData);

        var qUnit = QuantityAndUnit.getPriceAndUnit(ref name);


        var manufacturerRef = ManufacturerReferences.ContainsKey(manufacturer)
            ? ManufacturerReferences[manufacturer]
            : await CreateOrGetManufacturer(manufacturer);


        return new Product
        {
            Name = description.OnlyFirstCharToUpper(),
            Unit = qUnit.Unit.OnlyFirstCharToUpper(),
            ManufacturerId = manufacturerRef.Id,
            Store = StoreReference,
            CategoryId = categoryId,
            PricePerUnit = Convert.ToDecimal(Math.Round(price / qUnit.Quantity, 2)),
            ImageUri = imageUrl
        };
    }

    protected override List<KeyValuePair<string, string[]>> GenerateRequests()
    {
        var requests = new List<KeyValuePair<string, string[]>>
        {
            new("Fructe/Legume", new[]
            {
                BaseIdUrl.Replace("%FILTER%",HttpUtility.UrlEncode("category:alimentare/fructe-legume"))
            }),
            new("Carne", new[]
            {
                BaseIdUrl.Replace("%FILTER%",HttpUtility.UrlEncode("category:alimentare/carne")),
                BaseIdUrl.Replace("%FILTER%",HttpUtility.UrlEncode("category:alimentare/peste"))
            }),
            new("Lactate/Oua", new[]
            {
                BaseIdUrl.Replace("%FILTER%",HttpUtility.UrlEncode("category:alimentare/lactate"))
            }),
            new("Mezeluri", new[]
            {
                BaseIdUrl.Replace("%FILTER%",HttpUtility.UrlEncode("category:alimentare/mezeluri"))
            })
        };


        return requests;
    }

    protected override async Task<IEnumerable<Product>> InterpretResponse(HttpResponseMessage responseMessage, int categoryId)
    {
        var products = new List<Product>();


        var responseMessageContent = responseMessage.Content;
        var stringAsync = await responseMessageContent.ReadAsStringAsync();
        var result = JObject.Parse(stringAsync);

        var resultIds = result["resultIds"]!;


        var dataRequestUrls = GenerateDataUrls(resultIds.ToList());

        foreach (var url in dataRequestUrls)
        {
            try
            {
                var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
                requestMessage.Headers.Add("calltreeid", "a");

                var response = await HttpClient.SendAsync(requestMessage);

                var dataJson = JObject.Parse(await response.Content.ReadAsStringAsync());

                foreach (var item in dataJson["result"]!.Children().ToList())
                {
                    Product? product;
                    if ((product = await TryExtractProduct(item, categoryId)) is not null)
                        products.Add(product);
                }
            }
            catch
            {
            }
        }

        return products;
    }
}