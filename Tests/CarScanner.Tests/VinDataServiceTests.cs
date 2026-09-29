using CarScanner.Api.Services;

namespace CarScanner.Tests;

public sealed class VinDataServiceTests
{
    [Fact]
    public void ParseLots_ReadsOfficialMediaAndPricingShape()
    {
        const string vin = "1G1RB6E42DU101631";
        var json = """
        {
          "data": [
            {
              "platform": "copart",
              "lot_number": "12345678",
              "vin": "1G1RB6E42DU101631",
              "damage": "Front End",
              "pricing": {
                "sale_price_usd": 4750,
                "estimated_retail_value_usd": 9200,
                "repair_cost_usd": 3100
              },
              "media": {
                "thumbs_count": 2,
                "photos": [
                  "https://images.example.test/vehicle/1.jpg",
                  "https://images.example.test/vehicle/2.jpg"
                ]
              }
            }
          ]
        }
        """;

        var result = VinDataService.ParseLots(json, vin);

        var lot = Assert.Single(result);
        Assert.Equal("Copart", lot.AuctionName);
        Assert.Equal("Front End", lot.PrimaryDamage);
        Assert.Equal(4750m, lot.SoldPrice?.Amount);
        Assert.Equal(9200m, lot.EstimatedRetailValue?.Amount);
        Assert.Equal(3100m, lot.RepairCost?.Amount);
        Assert.Equal(2, lot.PhotoCount);
        Assert.Equal(2, lot.ImageUrls.Count);
    }

    [Fact]
    public void ParseLots_IgnoresSearchResultsForAnotherVin()
    {
        const string json = """
        {
          "data": [
            {
              "platform": "iaai",
              "lot_number": "87654321",
              "vin": "5N1BV28U14N386235",
              "media": { "photos": ["https://images.example.test/vehicle/1.jpg"] }
            }
          ]
        }
        """;

        var result = VinDataService.ParseLots(json, "1G1RB6E42DU101631");

        Assert.Empty(result);
    }
}
