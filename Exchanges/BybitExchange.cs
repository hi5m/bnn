// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Binance.Spot;
using Bnncmd.Strategy;
using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using Bybit.Net.Objects.Options;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Bnncmd
{
    internal class BybitExchange : AbstractExchange
    {
        public BybitExchange() : base()
        {
            // var httpClient = BnnUtils.BuildLoggingClient();
            var ak = "HwvJY5PgJMQ" + "J48PZ4m";
            var asc = "rXudNFnVWEIA3LBa" + "QVgXMkHMjFNi5fYAEfhz";
            var options = Options.Create(new BybitRestOptions
            {
                OutputOriginalData = true,
                ApiCredentials = new ApiCredentials(ak, asc)
            });
            _client = new Bybit.Net.Clients.BybitRestClient(null, null, options);
        }

        public override string Name { get; } = "Bybit";
        public override int Code { get; } = 1;
        public override decimal SpotTakerFee { get; } = 0.1M; // 00
        public override decimal SpotMakerFee { get; } = 0.1M;
        public override decimal FuturesTakerFee { get; } = 0.055M;
        public override decimal FuturesMakerFee { get; } = 0.02M;

        private readonly Bybit.Net.Clients.BybitRestClient _client;

        public override decimal GetDayFundingRate(string symbol)
        {
            symbol += "USDT";
            var bybitClient = new Bybit.Net.Clients.BybitRestClient();
            var bybitData = bybitClient.V5Api.ExchangeData.GetFundingRateHistoryAsync(Category.Linear, symbol, DateTime.Now.AddDays(-FundingRateDepth), DateTime.Now).Result.Data;
            if (bybitData == null) return decimal.MinValue;
            var bybitRates = bybitData.List;
            if (bybitRates.Length == 0) return decimal.MinValue;

            var fundingInterval = bybitRates[0].Timestamp.Hour - bybitRates[1].Timestamp.Hour;
            // Console.WriteLine($"fundingInterval: {bybitRates[0].Timestamp} - {bybitRates[1].Timestamp} = {fundingInterval} * {bybitRates[0].FundingRate * 100}");
            var currFundingRate = bybitRates[0].FundingRate * 100 * 24 / fundingInterval;
            return currFundingRate;

            /* decimal sumFundingRate = 0;
            foreach (var r in bybitRates)
            {
                sumFundingRate += r.FundingRate * 100;
                Console.WriteLine($"      {r.Timestamp}: {r.FundingRate * 100}");
            }
            return sumFundingRate / (decimal)FundingRateDepth;*/
            // Console.WriteLine($"3-days bybit accumulated funding rate: {sumFundingRate}");
        }

        private void GetPageProducts(int pageNum, List<EarnProduct> products, decimal minApr)
        {
            Console.WriteLine($"{Exchange.Bybit.Name} - Page {pageNum}...");
            var handler = new HttpClientHandler()
            {
                // AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            using var bbClient = new HttpClient(handler);
            bbClient.DefaultRequestHeaders.Clear();
            bbClient.DefaultRequestHeaders.Add("Cookie", "ttcsid_CMEEMQRC77UBHLCRLFPG=0");
            bbClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br, zstd");
            bbClient.DefaultRequestHeaders.Add("accept", "*/*");
            bbClient.DefaultRequestHeaders.Add("accept-language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            bbClient.DefaultRequestHeaders.Add("origin", "https://www.bybit.com");
            bbClient.DefaultRequestHeaders.Add("sec-fetch-site", "same-origin");
            bbClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");

            var request = new HttpRequestMessage(HttpMethod.Post, "https://www.bybit.com/x-api/s1/byfi/get-saving-homepage-product-cards");
            var jsonRequest = "{\"product_area\":[0],\"page\":" + pageNum + ",\"limit\":20,\"product_type\":6,\"sort_apr\":1,\"fixed_saving_version\":1}"; // "{\"product_area\":[0],\"fixed_saving_version\":1}"
            request.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");//CONTENT-TYPE header
            var bbEarnString = bbClient.SendAsync(request).Result.Content.ReadAsStringAsync().Result;

            // Console.WriteLine("==============\n\r" + bbEarnString + "\n\r==============");

            dynamic? earnPageData = JsonConvert.DeserializeObject(bbEarnString.Trim()) ?? throw new Exception("bybit page earn returned no data");
            var coinProducts = earnPageData.result.coin_products;
            foreach (var cp in coinProducts)
            {
                var savingProducts = cp.saving_products;
                foreach (var sp in savingProducts)
                {
                    if ((sp.display_status == 2) || (sp.display_status == 6)) continue; // sold out
                    // "product_area": 1, || "is_display_countdown": true, - Earn New User Exclusive

                    string aprStr = sp.apy;
                    aprStr = aprStr[..^1];
                    var apr = decimal.Parse(aprStr);
                    if (apr < minApr) continue;

                    string staking_term = sp.staking_term;
                    // var term = int.Parse(staking_term);

                    string dateStartStr = sp.subscribe_start_at + "000";
                    var startDate = BnnUtils.FormatUnixTime(long.Parse(dateStartStr), false);
                    string dateEndStr = sp.subscribe_end_at + "000";
                    var endDate = BnnUtils.FormatUnixTime(long.Parse(dateEndStr), false);
                    decimal share = sp.total_deposit_share;
                    // Console.WriteLine($"coin: {sp.coin}, apr: {apr}, term: {term}, tag: [{sp.product_tag_info.display_tag_key}], status: {sp.display_status}, period: {startDate} - {endDate}, share: {share:0.###}");

                    string productName = sp.product_tag_info.display_tag_key ?? sp.coin;
                    productName = productName.Replace("_Tag", string.Empty);
                    var product = new EarnProduct(Exchange.Bybit, productName, apr, "from page - locked");
                    if (sp.staking_term != null) product.Term = sp.staking_term;
                    products.Add(product);
                }
            }
        }

        private void GetBybitApiProducts(List<EarnProduct> products, decimal minApr)
        {
            Console.WriteLine($"{Exchange.Bybit.Name} - Api...");
            var apiProducts = _client.V5Api.Earn.GetProductInfoAsync(Bybit.Net.Enums.EarnCategory.FlexibleSaving); // .Result.Data.List
            foreach (var p in apiProducts.Result.Data.List)
            {
                var apr = decimal.Parse(p.EstimateApr.Substring(0, p.EstimateApr.Length - 1));// / 100; // 	"4.79%"
                if (apr < minApr) continue;
                var product = new EarnProduct(Exchange.Bybit, p.Asset, apr);
                product.Comment = "from api - flexible";
                products.Add(product);
                // Console.WriteLine($"{p.Asset}: {apr * 100}%");
            }

            apiProducts = _client.V5Api.Earn.GetProductInfoAsync(Bybit.Net.Enums.EarnCategory.OnChain); // .Result.Data.List
            foreach (var p in apiProducts.Result.Data.List)
            {
                var apr = decimal.Parse(p.EstimateApr.Substring(0, p.EstimateApr.Length - 1));// / 100; // 	"4.79%"
                if (apr < minApr) continue;
                var product = new EarnProduct(Exchange.Bybit, p.Asset, apr, "from api - on-chain");
                products.Add(product);
            }
        }

        public override void GetEarnProducts(List<EarnProduct> products, decimal minApr)
        {
            GetPageProducts(1, products, minApr);
            GetPageProducts(2, products, minApr);
            GetBybitApiProducts(products, minApr);
        }

        public override void EnterShort(string symbol, decimal amount)
        {
            throw new NotImplementedException();
        }

        /*private decimal CheckBalance(string coin, AccountType accountType)
        {
            var accInfo = _bybitClient.V5Api.Account.GetAllAssetBalancesAsync(accountType, null, coin).Result; // "USDT,USDC,BTC,ETH,BOMB,XO,LA"
            if ((accInfo.Error != null) && (!accInfo.Success)) throw new Exception(accInfo.Error.Message);
            if (accInfo.Data == null) throw new Exception("AccountInfo returned no data");

            foreach (var b in accInfo.Data.Balances)
            {
                // Console.WriteLine($"{b.Asset}, WalletBalance: {b.WalletBalance}, TransferBalance: {b.TransferBalance}, b: {b}");
                return b.WalletBalance ?? 0;
            }
            return 0;
        }*/

        public override decimal CheckSpotBalance(string? coin = null)
        {
            coin ??= UsdtName;
            var assetIfo = _client.V5Api.Account.GetAssetBalanceAsync(AccountType.Unified, coin).Result;
            if (!assetIfo.Success && (assetIfo.Error != null)) throw new Exception($"{Name} exception: {assetIfo.Error.Message}");
            return assetIfo.Data.Balances.WalletBalance ?? 0;
            // Console.WriteLine($"{Name} contract rest: {assetIfo.Data.Balances.WalletBalance}");
            // return CheckBalance(coin, AccountType.Unified); // "USDT,USDC,BTC,ETH,BOMB,XO,LA"

            /* var accInfo = _bybitClient.V5Api.Account.GetAllAssetBalancesAsync(AccountType.Unified, null, coin).Result;
            if ((accInfo.Error != null) && (!accInfo.Success)) throw new Exception(accInfo.Error.Message);
            if (accInfo.Data == null) throw new Exception("AccountInfo returned no data");

            foreach (var b in accInfo.Data.Balances)
            {
                // if ((b.Available == 0) || (b.Locked == 0)) continue;
                Console.WriteLine($"{b.Asset}, WalletBalance: {b.WalletBalance}, TransferBalance: {b.TransferBalance}, b: {b}");
                return b.WalletBalance ?? 0;
            }
            return 0;*/
        }

        public override decimal CheckFuturesBalance(string? coin = null)
        {
            coin ??= UsdtName;
            var assetIfo = _client.V5Api.Account.GetAssetBalanceAsync(AccountType.Contract, coin).Result;
            return assetIfo.Data.Balances.WalletBalance ?? 0;
        }

        public override decimal GetSpotPrice(string coin)
        {
            var priceInfo = _client.V5Api.ExchangeData.GetSpotTickersAsync(coin + UsdtName).Result;
            if (!priceInfo.Success && (priceInfo.Error != null))
            {
                if (priceInfo.Error.Code == 10001) return 0;
                throw new Exception(priceInfo.Error.Message + " / " + priceInfo.Error.Code.ToString());
            }
            if (priceInfo.Data.List.Length == 0) return 0;
            // if (priceInfo.Data.List.Length == 0) throw new Exception($"{Name} spot price returned empty list");
            return priceInfo.Data.List[0].BestBidPrice ?? 0;
        }

        public override decimal FindFunds(string coin, bool forSpot = true, decimal amount = 0)
        {
            decimal sum = 0;
            WebCallResult<BybitSingleAssetBalance> assetInfo;

            if (forSpot)
            {
                assetInfo = _client.V5Api.Account.GetAssetBalanceAsync(AccountType.Contract, UsdtName).Result;
                if ((assetInfo.Error != null) && !assetInfo.Success) throw new Exception(assetInfo.Error.Message);
                sum += assetInfo.Data.Balances.WalletBalance ?? 0;
                Console.WriteLine($"   futures rest: {assetInfo.Data.Balances.WalletBalance}");
            }
            else
            {
                assetInfo = _client.V5Api.Account.GetAssetBalanceAsync(AccountType.Spot, UsdtName).Result;
                if ((assetInfo.Error != null) && !assetInfo.Success) throw new Exception(assetInfo.Error.Message);
                sum += assetInfo.Data.Balances.WalletBalance ?? 0;
                Console.WriteLine($"   spot rest: {assetInfo.Data.Balances.WalletBalance}");
            }

            assetInfo = _client.V5Api.Account.GetAssetBalanceAsync(AccountType.Fund, UsdtName).Result;
            if ((assetInfo.Error != null) && !assetInfo.Success) throw new Exception(assetInfo.Error.Message);
            sum += assetInfo.Data.Balances.WalletBalance ?? 0;
            Console.WriteLine($"   fund rest: {assetInfo.Data.Balances.WalletBalance}");

            var earnPositions = _client.V5Api.Earn.GetStakedPositionsAsync(EarnCategory.FlexibleSaving, null, UsdtName).Result;
            if ((earnPositions.Error != null) && !earnPositions.Success) throw new Exception(earnPositions.Error.Message);
            var earnRest = earnPositions.Data.List.Length > 0 ? earnPositions.Data.List[0].Quantity : 0;
            sum += earnRest;
            var toAccount = forSpot ? AccountType.Spot : AccountType.Contract;
            if (amount > 0)
            {
                var tranferResult = _client.V5Api.Account.CreateInternalTransferAsync(UsdtName, amount, AccountType.Fund, toAccount).Result;
                if ((tranferResult.Error != null) && !tranferResult.Success) throw new Exception("error while transfer from earn account: " + tranferResult.Error.Message);
                else Console.WriteLine($"transfered from earn: {tranferResult.Data.Status}, {tranferResult.Data.TransferId}");
            }
            else Console.WriteLine($"   earn rest: {earnPositions.Data.List[0].Quantity}");

            return sum;
        }

        public override decimal GetMaxLimit(string coin, bool isSpot)
        {
            if (!isSpot) throw new NotImplementedException();
            var symbolInfo = _client.V5Api.ExchangeData.GetSpotSymbolsAsync(coin + UsdtName).Result;
            if (!symbolInfo.Success && (symbolInfo.Error != null)) throw new Exception(symbolInfo.Error.Message);
            if ((symbolInfo.Data.List == null) || (symbolInfo.Data.List.Length == 0)) throw new Exception($"{Name} GetMaxLimit return no data");
            var filter = symbolInfo.Data.List[0].LotSizeFilter ?? throw new Exception();
            return filter.MaxOrderQuantity;
        }

        public override void GetFundingRates(List<FundingRate> rates, decimal minRate)
        {
            var symbolsInfo = _client.V5Api.ExchangeData.GetLinearInverseTickersAsync(Category.Linear).Result;
            if (!symbolsInfo.Success && (symbolsInfo.Error != null)) throw new Exception(symbolsInfo.Error.Message);
            foreach (var s in symbolsInfo.Data.List)
            {
                if (!s.Symbol.EndsWith(UsdtName) || (s.FundingRate <= minRate / 100)) continue;
                var fr = new FundingRate(this, s.Symbol, s.FundingRate * 100 ?? 0);
                rates.Add(fr);
            }
        }

        public override decimal GetOrderBookTicker(string coin, bool isSpot, bool isAsk)
        {
            WebCallResult<decimal?> priceInfo;
            if (isSpot)
            {
                var spotTickers = _client.V5Api.ExchangeData.GetSpotTickersAsync(coin + UsdtName).Result;
                // Console.WriteLine($"bybit: {spotTickers}");
                decimal? bestPrice = 0;
                if (spotTickers.Data != null) bestPrice = spotTickers.Data.List.Length == 0 ? 0 : (isAsk ? spotTickers.Data.List[0].BestAskPrice : spotTickers.Data.List[0].BestBidPrice);
                priceInfo = spotTickers.As(bestPrice);
            }
            else
            {
                var futuresTickers = _client.V5Api.ExchangeData.GetLinearInverseTickersAsync(Category.Linear, coin + UsdtName).Result;
                priceInfo = futuresTickers.As(futuresTickers.Data.List.Length == 0 ? 0 : (isAsk ? futuresTickers.Data.List[0].BestAskPrice : futuresTickers.Data.List[0].BestBidPrice));
            }

            if (!priceInfo.Success && (priceInfo.Error != null))
            {
                if (priceInfo.Error.Code == 10001) return 0;
                throw new Exception($"{Name} best spot price returned error: {priceInfo.Error.Message} / {priceInfo.Error.Code}");
            }
            return priceInfo.Data ?? 0;
        }
    }
}
