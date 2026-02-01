// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Policy;
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
using Newtonsoft.Json.Linq;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Microsoft.Extensions.Configuration;

namespace Bnncmd
{
    internal class
    BybitExchange : AbstractExchange
    {
        public BybitExchange() : base()
        {
            // var httpClient = BnnUtils.BuildLoggingClient();
            var ak = AccountManager.Config.GetValue<string>("TKBBT") ?? string.Empty;
            var asc = AccountManager.Config.GetValue<string>("TSBBT") ?? string.Empty;
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

        public override HedgeInfo[] GetDayFundingRate(string coin)
        {
            var symbol = coin + "USDT";
            // var bybitClient = new Bybit.Net.Clients.BybitRestClient();
            var bybitData = _client.V5Api.ExchangeData.GetFundingRateHistoryAsync(Category.Linear, symbol, DateTime.Now.AddDays(-FundingRateDepth), DateTime.Now).Result.Data;
            if (bybitData == null) return [];
            var bybitRates = bybitData.List;
            if (bybitRates.Length < 2) return [];

            var fundingInterval = bybitRates[0].Timestamp.Hour - bybitRates[1].Timestamp.Hour;
            var ratesArr = bybitRates.Select(r => r.FundingRate).Take(10).ToArray(); // then process EMA
            return [new HedgeInfo(this)
            {
                Symbol = symbol,
                EmaFundingRate = 100 * GetEmaFundingRate(ratesArr) * 24 / fundingInterval,
                Fee = FuturesMakerFee
            }];

            // var fundingInterval = bybitRates[0].Timestamp.Hour - bybitRates[1].Timestamp.Hour;
            // Console.WriteLine($"fundingInterval: {bybitRates[0].Timestamp} - {bybitRates[1].Timestamp} = {fundingInterval} * {bybitRates[0].FundingRate * 100}");
            // var currFundingRate = bybitRates[0].FundingRate * 100 * 24 / fundingInterval;
            // return currFundingRate;

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
            var bbEarnString = DownloadWithCurl("get-bybit-fixed-earn.bat");

            /*var handler = new HttpClientHandler()
            {
                // AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            using var bbClient = new HttpClient(handler);
            bbClient.DefaultRequestHeaders.Clear();
            // bbClient.DefaultRequestHeaders.Add("Cookie", "ttcsid_CMEEMQRC77UBHLCRLFPG=0;");
            bbClient.DefaultRequestHeaders.Add("Cookie", "_abck=9C6341B6EFDBF3E4FAC39C2BFB1B4C6E~0~YAAQh0hlX+XwaL6bAQAA/Et23w9XCcXPq2QyBX/XL06WRKf90Euxs0MlAInkW0cc1/xiq/Ec9Ef8y7zjca+yQpbYyNRj3OHmtsqI3gGgO6SfP1pVo4aJTZPjSH9W1zwo/O18amahYjY4opVeWyj2AHg3bthb8HPVs2vHA2yQ+C6DNwUsY+vh0z16iNem1cJOsyFAeGqI38Rzx80CCkRtCpMZy979KQGDS/34WpaMDODPfHcCZdfj1Mxw3x7xs5VJZlSOA61lKGZIlSL1152Lp7QTFQjxkdoOdShK5u6owWk1E71muXuY02t9IeCcuD5Al30Lh+tJtz9/cjkOhBO4TRZ/8DYSUdVOgS8rlPW+098Xst3KOPRTrjn6R/zbK4FVPW+tXyOdOb/H0U272S7UULaxsypZka0JkNtUTUxtqPOz0cuuhQhotnJlo+R53EpmDeSBDhBWVScd2NfBg7D/BbcJT0I5Mcyco/nlW0NpXgk8n86VZPTQZx1JBSgMg7sk9iI+whF0B4Bg2pPxnNa4EfDii+5qPZ9xBjmM8Trb48tJmT3qSdZVFu4YQylSaNwpTF+1+gf5DD4kNBDhc+qm7Ro300FSaaYCUJRKdQGqmszAb/S4SbpBADnyNULqhwSVwI0XqcQkmrwJSSSkFNdtQEnjtUzkzpjPU4+tkEwc5JM9xbcAd29q+P8rszv3348y4ii+kDcxY8zVfOmiGTmTjuzOxg==~-1~-1~-1~AAQAAAAF^%^2f^%^2f^%^2f^%^2f^%^2fyVtF3SHEWOQkxqcRGeJJvQAgM9yPCNV5sqKz2b1rXO8qCr+EHNBEhXgv5g5XzGDskIgwaiKbVtHNKWN1+NdUJF1LHvValvn3mvka51cJQT+VEW9JXgf5WJIoM1tcoToK+VTnKQ^%^3d~-1; sensorsdata2015jssdkcross=^%^7B^%^22distinct_id^%^22^%^3A^%^22196d0b787c02e6-08ce465fa85956-f5d5728-3686400-196d0b787c25d4^%^22^%^2C^%^22first_id^%^22^%^3A^%^22^%^22^%^2C^%^22props^%^22^%^3A^%^7B^%^22^%^24latest_traffic_source_type^%^22^%^3A^%^22^%^E8^%^87^%^AA^%^E7^%^84^%^B6^%^E6^%^90^%^9C^%^E7^%^B4^%^A2^%^E6^%^B5^%^81^%^E9^%^87^%^8F^%^22^%^2C^%^22^%^24latest_search_keyword^%^22^%^3A^%^22^%^E6^%^9C^%^AA^%^E5^%^8F^%^96^%^E5^%^88^%^B0^%^E5^%^80^%^BC^%^22^%^2C^%^22^%^24latest_referrer^%^22^%^3A^%^22https^%^3A^%^2F^%^2Fwww.google.com^%^2F^%^22^%^2C^%^22_a_u_v^%^22^%^3A^%^220.0.6^%^22^%^7D^%^2C^%^22identities^%^22^%^3A^%^22eyIkaWRlbnRpdHlfY29va2llX2lkIjoiMTk2ZDBiNzg3YzAyZTYtMDhjZTQ2NWZhODU5NTYtZjVkNTcyOC0zNjg2NDAwLTE5NmQwYjc4N2MyNWQ0In0^%^3D^%^22^%^2C^%^22history_login_id^%^22^%^3A^%^7B^%^22name^%^22^%^3A^%^22^%^22^%^2C^%^22value^%^22^%^3A^%^22^%^22^%^7D^%^7D; deviceId=dd7b719f-0220-d1b7-f532-8bae9eebc7a9; _fwb=894KpiTYZQSIiHGm5Yx8Al.1747258412261; wcs_bt=135fb0af9baad90:1768980965^|17470ac91156420:1768915718; _ga_SPS4ND2MGC=GS2.1.s1768980649^$o39^$g1^$t1768980966^$j20^$l0^$h0; _ga=GA1.1.890256513.1747258430; tmr_lvid=eb6ff7ae2fc6a529283bd17b7bf17963; tmr_lvidTS=1747258430200; _ym_uid=1747258430839307566; _ym_d=1747258430; _tt_enable_cookie=1; _ttp=01JV8BFN75M3CKD94GVAWSG07T_.tt.1; ttcsid_CMEEMQRC77UBHLCRLFPG=1754776208618::FoegtIYbxx5uoB_VMRfG.23.1754776728542; ttcsid=1754776208620::dBSkZc7qo_fcjADYLiWt.21.1754776723658; cto_bundle=Qtud4l91Uk9JOXh3Rno2ZVhUQUNwVEMlMkZtcHZjT3B5bXY5U1BuczRBY0NCN0JISGVmc2J2dzclMkJYY2NWaFc0VGF0TmN1NHVyJTJGR3drUHQ3enQwdiUyQnRRTDV0OW10SVcxQzRmajROWUVOUk9pQlk4eXVYR0JlMlVDODhvVUQlMkI4VHM3V1RqWiUyQm9Ib1VPMEUlMkJ1NE03JTJGRVlNejRmNGp3JTNEJTNE; g_state=^{^\\^\"i_p^\\^\":1770885027314,^\\^\"i_l^\\^\":4,^\\^\"i_ll^\\^\":1768980984589,^\\^\"i_b^\\^\":^\\^\"k7pgk7hNczYB6p05IYazdZS3Qd1UsmRe+YLZpNKiT/w^\\^\",^\\^\"i_e^\\^\":^{^\\^\"enable_itp_optimization^\\^\":0^}^}; sensorsdata2015jssdkchannel=^%^7B^%^22prop^%^22^%^3A^%^7B^%^22_sa_channel_landing_url^%^22^%^3A^%^22^%^22^%^7D^%^7D; _by_l_g_d=fd920a5f-d4a4-909f-c1c2-2774ecc69772; tx_token_time=1768981031184; cookies_uuid_report=e8c9a38d-566d-4293-97e2-029e56f7cc97; first_collect=true; trace_id_time=1768981031259; _gcl_au=1.1.303677725.1768462780; tx_token_current=BNE; BYBIT_REG_REF_prod=^{^\\^\"lang^\\^\":^\\^\"en-US^\\^\",^\\^\"g^\\^\":^\\^\"fd920a5f-d4a4-909f-c1c2-2774ecc69772^\\^\",^\\^\"referrer^\\^\":^\\^\"www.google.com/^\\^\",^\\^\"source^\\^\":^\\^\"google.com^\\^\",^\\^\"medium^\\^\":^\\^\"other^\\^\",^\\^\"url^\\^\":^\\^\"https://www.bybit.com/en/earn/home/^\\^\",^\\^\"last_refresh_time^\\^\":^\\^\"Wed, 21 Jan 2026 07:30:32 GMT^\\^\",^\\^\"ext_json^\\^\":^{^\\^\"dtpid^\\^\":null,^\\^\"click_id^\\^\":null^}^}; ak_bmsc=CCE3CD88B377605A5BA0BB83BAF5B490~000000000000000000000000000000~YAAQh0hlXwL/aL6bAQAAhHh23x53+UmCyqKhWrAXkNa5DSWdNYd07IlqqmkyZ9XUL/gAIMddgd6nGPIadLuwVE77Dsc0SBMqqgzI/krIqbWj+K8lfXiME7LapYojjjma7FE4haAJPo6v8ebV2RnK8PQiVZY78jI3pzyJEQuctG1xj0NfqD3nyHba+ypjzBY+Xsb7D+0hdfJU6av2rDuaKkAqcEZSBNK+/F3xM7Cl30FUPCGfB7RsTrFIYhJfefqvwTBzc+hPJRZTE+O8Zcd8o/2I9n0cZePw261ndI/ETeeYT8u/8XcMutUL/KffbYjhuwVEnTy23L9p/7KeUjEuWfc/H65XN7GEJribS0jk88atAd6ucysdDnJexbs2yk1Yx2kN0sBKqhrTX0JDm5/Ig/8EFGzzJ+E9zECucCTLPa5R7cPtaINunxbqJpetOg+4QRuvWLGwWwD8pU46mBB6hCAc/ikf/Ffa6hFWF1E=; bm_sz=7959C6E66231C75343AA477E06AD3447~YAAQh0hlX+ajar6bAQAA/NF63x6M2rmCn3Tl0rY3Zlee6u0M91UqLmiKjeww9YBCGiE6y4+j7cbZ8AsrrtwZEtbKXTY5gVD0fKT9b8VQKsCBpk9ZwrQWBiLYD6c1hSrrUMvPq5UB9PSWRuJwhbzUBq0nmjflkyT/BAtMYcEKbeiu7+tnJEgiJ0n2G8VQ0yb7+SPr6wuDGkM48YzdgHNSfrQG3ATHDfigJYgmACntPu/JYJGcxH3RXKPksEMWuYss0QxPsMQJpf1niVDOJLI9bFvb9VJJ6HI95qvEGxxrBGBRpaWKVdO7FV+KFfhysoKY2TPYeH4u+RZ5gs8kL8Qy+ZzfQ+EW9d6QYnYaMjXYINpFKo0nYO9N/D1fvJ4673Vi5LiMiqDcaO6e3A9GgAe7snuty5ShKwengYzh~3686706~4473413; bm_mi=6CE86E5AFE8FC614F898FF4BFE41B07C~YAAQh0hlXy7xaL6bAQAArEx23x6gz6DAwoeO2rVQzk4yQi/w12Iv/GsGvBQONypEMiLOifdX8cYaciJOZI6TC9yWavmnSb00LXx9K38nM9uCEQdZGf6GPC++NdQtsSu8yvP8zlkaBHb+TEUbstYk7Q0snQp2jHoMm/KQw6BLRe0NX/pEVBMObpEhRrL8hgnAihqjzBEGrXoEoKWOtVy0QmX8XOR7y+ESQWaILkkfyGy+dqANU0RtyXbm6g+tVlCVc0W/3Z97ZHoP1YrWrNfD2ItGIWq3MpMSQxEPCEK5TsfLwt5xHCZP5Efgyls1Fn8p61C2Ty0hofjaPk5gCRip~1; bm_sv=06F23B767E2FC3ED54A54E7A448C9A77~YAAQh0hlX01acL6bAQAA1RmK3x4SZppX54RGD5oLqcl5lQvLKCr2jlDxQMhapXKF99GW039kmhnCQTdDDAXsCVY79T6SHA4ABiGguMHQZslRWUo+veLY8YPpGmnXQowRm5f5NLvT9uofRA6FMmfV1n+Cu6Udy0ePoUfUKCQ72rIGNhTww8E1JRI2/iDlOrh6u8PC8WCJfou7B4Iw+QLxjrpQ5bNFX0QJJaTIA916gtrO0SYuXEZR5MsDSvnPQiQ7+w==~1");
            bbClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br, zstd");
            bbClient.DefaultRequestHeaders.Add("Accept", ");
            bbClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            bbClient.DefaultRequestHeaders.Add("Origin", "https://www.bybit.com");
            bbClient.DefaultRequestHeaders.Add("Sec-Fetch-Site", "same-origin");
            bbClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");

            // var request = new HttpRequestMessage(HttpMethod.Post, "https://www.bybit.com/x-api/s1/byfi/get-saving-homepage-product-cards");
            var request = new HttpRequestMessage(HttpMethod.Post, "https://www.bybit.com/x-api/s1/byfi/get-easy-earn-product-list");
            // var jsonRequest = "{\"product_area\":[0],\"page\":" + pageNum + ",\"limit\":20,\"product_type\":6,\"sort_apr\":1,\"fixed_saving_version\":1}"; // "{\"product_area\":[0],\"fixed_saving_version\":1}"
            // var jsonRequest = "{\"product_area\":[0],\"page\":" + pageNum + ",\"limit\":20,\"product_type\":6,\"sort_apr\":1,\"fixed_saving_version\":1}"; // "{\"product_area\":[0],\"fixed_saving_version\":1}"
            var jsonRequest = "{\"page\":1,\"limit\":50,\"sort_type\":0,\"fixed_saving_version\":1}";
            request.Content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");//CONTENT-TYPE header
            var bbEarnString = bbClient.SendAsync(request).Result.Content.ReadAsStringAsync().Result;*/

            // Console.WriteLine("==============\n\r" + bbEarnString + "\n\r==============");

            dynamic? earnPageData = JsonConvert.DeserializeObject(bbEarnString.Trim()) ?? throw new Exception("bybit page earn returned no data");
            var coinProducts = earnPageData.result.coin_products;
            foreach (var cp in coinProducts)
            {
                // Console.WriteLine($"{cp.coin}: {cp.apy}%");
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
            var apiProducts = _client.V5Api.Earn.GetProductInfoAsync(EarnCategory.FlexibleSaving); // .Result.Data.List
            foreach (var p in apiProducts.Result.Data.List)
            {
                var apr = decimal.Parse(p.EstimateApr.Substring(0, p.EstimateApr.Length - 1));// / 100; // 	"4.79%"
                if (apr < minApr) continue;
                var product = new EarnProduct(Exchange.Bybit, p.Asset, apr);
                product.Comment = "from api - flexible";
                products.Add(product);
                // Console.WriteLine($"{p.Asset}: {apr * 100}%");
            }

            apiProducts = _client.V5Api.Earn.GetProductInfoAsync(EarnCategory.OnChain); // .Result.Data.List
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
            // GetPageProducts(2, products, minApr);
            GetBybitApiProducts(products, minApr);
        }

        public override void EnterShort(string symbol, decimal amount, string stableCoin = EmptyString)
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
            coin ??= StableCoin.USDT;
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
            coin ??= StableCoin.USDT;
            var assetIfo = _client.V5Api.Account.GetAssetBalanceAsync(AccountType.Contract, coin).Result;
            return assetIfo.Data.Balances.WalletBalance ?? 0;
        }

        public override decimal GetSpotPrice(string coin)
        {
            var priceInfo = _client.V5Api.ExchangeData.GetSpotTickersAsync(coin + StableCoin.USDT).Result;
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
                assetInfo = _client.V5Api.Account.GetAssetBalanceAsync(AccountType.Contract, StableCoin.USDT).Result; // Unified?
                if ((assetInfo.Error != null) && !assetInfo.Success) throw new Exception(assetInfo.Error.Message);
                sum += assetInfo.Data.Balances.WalletBalance ?? 0;
                Console.WriteLine($"   Futures rest: {assetInfo.Data.Balances.WalletBalance}");
            }
            else
            {
                assetInfo = _client.V5Api.Account.GetAssetBalanceAsync(AccountType.Unified, StableCoin.USDT).Result;
                if ((assetInfo.Error != null) && !assetInfo.Success) throw new Exception(assetInfo.Error.Message);
                sum += assetInfo.Data.Balances.WalletBalance ?? 0;
                Console.WriteLine($"   Unified wallet rest: {assetInfo.Data.Balances.WalletBalance}");
            }

            assetInfo = _client.V5Api.Account.GetAssetBalanceAsync(AccountType.Fund, StableCoin.USDT).Result;
            if ((assetInfo.Error != null) && !assetInfo.Success) throw new Exception(assetInfo.Error.Message);
            sum += assetInfo.Data.Balances.WalletBalance ?? 0;
            Console.WriteLine($"   Fund rest: {assetInfo.Data.Balances.WalletBalance}");

            var earnPositions = _client.V5Api.Earn.GetStakedPositionsAsync(EarnCategory.FlexibleSaving, null, StableCoin.USDT).Result;
            if ((earnPositions.Error != null) && !earnPositions.Success) throw new Exception(earnPositions.Error.Message);
            var earnRest = earnPositions.Data.List.Length > 0 ? earnPositions.Data.List[0].Quantity : 0;
            sum += earnRest;
            var toAccount = forSpot ? AccountType.Spot : AccountType.Contract;
            if (amount > 0)
            {
                var tranferResult = _client.V5Api.Account.CreateInternalTransferAsync(StableCoin.USDT, amount, AccountType.Fund, toAccount).Result;
                if ((tranferResult.Error != null) && !tranferResult.Success) throw new Exception("error while transfer from earn account: " + tranferResult.Error.Message);
                else Console.WriteLine($"transfered from earn: {tranferResult.Data.Status}, {tranferResult.Data.TransferId}");
            }
            else Console.WriteLine($"   Earn rest: {earnPositions.Data.List[0].Quantity}");

            return sum;
        }

        public override decimal GetMaxLimit(string coin, bool isSpot, string stablecoin = EmptyString)
        {
            // throw new NotImplementedException();            
            // Task<WebCallResult<BybitResponse<object>>> symbolInfo = isSpot ? _client.V5Api.ExchangeData.GetSpotSymbolsAsync(coin + UsdtName).Result : _client.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(Category.Linear, coin + UsdtName).Result;
            // var symbolInfo = isSpot ? _client.V5Api.ExchangeData.GetSpotSymbolsAsync(coin + UsdtName).Result : _client.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(Category.Linear, coin + UsdtName).Result;
            if (isSpot)
            {
                var symbolInfo = _client.V5Api.ExchangeData.GetSpotSymbolsAsync(coin + StableCoin.USDT).Result;
                if (!symbolInfo.Success && (symbolInfo.Error != null)) throw new Exception(symbolInfo.Error.Message);
                if ((symbolInfo.Data.List == null) || (symbolInfo.Data.List.Length == 0)) throw new Exception($"{Name} GetMaxLimit return no data");
                var filter = symbolInfo.Data.List[0].LotSizeFilter ?? throw new Exception("GetMaxLimit: LotSizeFilter not fount");
                return filter.MaxOrderQuantity;
            }
            else
            {
                var symbolInfo = _client.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(Category.Linear, coin + StableCoin.USDT).Result;
                if (!symbolInfo.Success && (symbolInfo.Error != null)) throw new Exception(symbolInfo.Error.Message);
                if ((symbolInfo.Data.List == null) || (symbolInfo.Data.List.Length == 0)) throw new Exception($"{Name} GetMaxLimit return no data");
                var filter = symbolInfo.Data.List[0].LotSizeFilter ?? throw new Exception("GetMaxLimit: LotSizeFilter not fount");
                return filter.MaxOrderQuantity;
            }
        }

        public override decimal GetMinLimit(string coin, bool isSpot, string stablecoin = EmptyString) => throw new NotImplementedException();

        public override void GetFundingRates(List<FundingRate> rates, decimal minRate)
        {
            var symbolsInfo = _client.V5Api.ExchangeData.GetLinearInverseTickersAsync(Category.Linear).Result;
            if (!symbolsInfo.Success && (symbolsInfo.Error != null)) throw new Exception(symbolsInfo.Error.Message);
            foreach (var s in symbolsInfo.Data.List)
            {
                if (!s.Symbol.EndsWith(StableCoin.USDT) || (s.FundingRate <= minRate / 100)) continue;
                var fr = new FundingRate(this, s.Symbol, s.FundingRate * 100 ?? 0);
                rates.Add(fr);
            }
        }

        public override decimal GetOrderBookTicker(string coin, bool isSpot, bool isAsk)
        {
            WebCallResult<decimal?> priceInfo;
            if (isSpot)
            {
                var spotTickers = _client.V5Api.ExchangeData.GetSpotTickersAsync(coin + StableCoin.USDT).Result;
                // Console.WriteLine($"bybit: {spotTickers}");
                decimal? bestPrice = 0;
                if (spotTickers.Data != null) bestPrice = spotTickers.Data.List.Length == 0 ? 0 : (isAsk ? spotTickers.Data.List[0].BestAskPrice : spotTickers.Data.List[0].BestBidPrice);
                priceInfo = spotTickers.As(bestPrice);
            }
            else
            {
                var futuresTickers = _client.V5Api.ExchangeData.GetLinearInverseTickersAsync(Category.Linear, coin + StableCoin.USDT).Result;
                priceInfo = futuresTickers.As(futuresTickers.Data.List.Length == 0 ? 0 : (isAsk ? futuresTickers.Data.List[0].BestAskPrice : futuresTickers.Data.List[0].BestBidPrice));
            }

            if (!priceInfo.Success && (priceInfo.Error != null))
            {
                if (priceInfo.Error.Code == 10001) return 0;
                throw new Exception($"{Name} best spot price returned error: {priceInfo.Error.Message} / {priceInfo.Error.Code}");
            }
            return priceInfo.Data ?? 0;
        }

        public override void BuySpot(string coin, decimal amount) => throw new NotImplementedException();
    }
}
