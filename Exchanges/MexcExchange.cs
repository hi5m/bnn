// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.
using Bnncmd.Strategy;
using Bybit.Net.Objects.Models.V5;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using Mexc.Net.Clients;
using Mexc.Net.Enums;
using Mexc.Net.Objects.Models.Futures;
using Mexc.Net.Objects.Models.Spot;
using Mexc.Net.Objects.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.X509;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Bnncmd
{
    internal class MexcExchange : AbstractExchange
    {
        public MexcExchange() : base()
        {
            var ak = AccountManager.Config.GetValue<string>("TKMX") ?? string.Empty;
            var asc = AccountManager.Config.GetValue<string>("TSMX") ?? string.Empty;
            var options = Options.Create(new MexcRestOptions
            {
                OutputOriginalData = true,
                ApiCredentials = new ApiCredentials(ak, asc)
            });
            /* using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole()
                    .SetMinimumLevel(LogLevel.Trace);
            });*/

            // var loggedClient = BnnUtils.BuildLoggingClient();
            _apiClient = new MexcRestClient(null, null, options);
            var sockOptions = Options.Create(new MexcSocketOptions
            {
                OutputOriginalData = true,
                ApiCredentials = new ApiCredentials(ak, asc)
            });

            _socketClient = new MexcSocketClient(sockOptions, null);
        }

        private static string GetErrorText(int errorCode)
        {
            return errorCode switch
            {
                1002 => "Contract not activated",
                2011 => "Order quantity error",
                2008 => "The quantity is insufficient",
                _ => $"Unknown error",
            };
        }

        private readonly MexcRestClient _apiClient;

        private readonly MexcSocketClient _socketClient;

        public override string Name { get; } = "Mexc";
        public override int Code { get; } = 2;
        // public override decimal SpotTakerFee { get; } = 0.05M;
        // public override decimal SpotMakerFee { get; } = 0;
        public override decimal FuturesTakerFee { get; } = 0.02M;
        // public override decimal FuturesMakerFee { get; } = 0M;
        public override decimal FuturesMakerFee { get; } = 0.01M; // sometimes 0

        public override HedgeInfo[] GetDayFundingRate(string symbol)
        {
            throw new NotImplementedException();

            /* symbol += "_USDT";
            using var client = new HttpClient();
            var mexcFundingString = client.GetStringAsync($"https://contract.mexc.com/api/v1/contract/funding_rate/history?symbol={symbol}&page_size=72").Result;
            dynamic? mexcFundingData = JsonConvert.DeserializeObject(mexcFundingString.Trim()) ?? throw new Exception("mexc funding history returned no data");
            if (mexcFundingData.data.resultList.Count == 0) return decimal.MinValue;

            // last funding rate approximated to whole day
            long endTime = mexcFundingData.data.resultList[0].settleTime;
            long startTime = mexcFundingData.data.resultList[1].settleTime;
            var fundingInterval = BnnUtils.UnitTimeToDateTime(endTime).Hour - BnnUtils.UnitTimeToDateTime(startTime).Hour;
            decimal lastRate = mexcFundingData.data.resultList[0].fundingRate * 100 * 24 / fundingInterval;
            // var minCurrentRate = Math.Min(lastRate, currRate) * 100 * 24 / fundingInterval;

            // current funding rate
            var rateData = _apiClient.FuturesApi.ExchangeData.GetFundingRateAsync(symbol).Result;
            if (!rateData.Success && (rateData.Error != null)) throw new Exception(rateData.Error.Message);
            var currRate = rateData.Data.Data == null ? -100 : rateData.Data.Data.FundingRate * 100 * 24 / fundingInterval ?? -100;

            // averate 3-days curent rate
            decimal sumFundingRate = 0;
            foreach (var r in mexcFundingData.data.resultList)
            {
                long settleTime = r.settleTime;
                var settleDT = BnnUtils.UnitTimeToDateTime(settleTime);
                if (settleDT.AddDays(FundingRateDepth) < DateTime.Now) break;
                decimal rate = r.fundingRate;
                // Console.WriteLine($"      {settleDT}: {rate}");
                sumFundingRate += rate * 100;
            }
            var avg3DaysRate = sumFundingRate / (decimal)FundingRateDepth;
            // Console.WriteLine($"                mexc {symbol} avg 3-days rate: {avg3DaysRate}");
            Console.WriteLine($"   mexc dynamic rate: {avg3DaysRate:0.###} => {lastRate:0.###} => {currRate:0.###}");

            return lastRate; //  Math.Min(minCurrentRate, avg3DaysRate);*/
        }

        /*private static HttpClient CreateMexcClient()
        {
            var handler = new HttpClientHandler()
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:139.0) Gecko/20100101 Firefox/139.0");
            client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.5");
            client.DefaultRequestHeaders.Add("DNT", "1");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br, zstd");
            return client;
        }*/

        private static string GetMd5Digest(string utfStr)
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(utfStr);
            byte[] hashBytes = MD5.HashData(inputBytes);
            var sb = new StringBuilder();
            foreach (byte b in hashBytes)
            {
                sb.Append(b.ToString("x2"));
            }
            return sb.ToString();
        }

        private static void EnterShortBrowser()
        {
            /* var chromePageXOffset = -1;
            var chromePageYOffset = 85;
            var fieldWidth = 265;
            var fieldHeigth = 40;

            Cursor.Position = new Point(2150 + chromePageXOffset + (int)(fieldWidth / 2), 302 + chromePageYOffset + (int)(fieldHeigth / 2));

            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

            Thread.Sleep(5); // небольшая пауза между кликами

            // Второй клик
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

            SendKeys.SendWait("1"); // Эмулирует нажатие клавиши "A"
            SendKeys.SendWait("2"); // Эмулирует нажатие клавиши "A"
            SendKeys.SendWait("5"); // Эмулирует нажатие клавиши "A"*/

            // Exchange.Mexc.EnterShort("AGT", 0);
            // Exchange.Mexc.ScanFutures("GORK");*/
        }

        private static void EnterShortWeb(string coin, decimal amount)
        {
            /* var bestRealAsk = 0.045;
            var symbol = "TOWNS_USDT";
            amount = 22;

            var client = CreateMexcClient();

            var u_id = "WEB028ca744ad01eb4cff3d7c981a647ee66d6aac07ac79a16e3689f2be4d5f5bd7";
            var dateNow = "1754775079873"; //  BnnUtils.GetUnixNow().ToString();
            var g = GetMd5Digest(u_id + dateNow)[7..];

            var query = $"{{\"symbol\":\"{symbol}\",\"side\":3,\"openType\":1,\"type\":\"1\",\"vol\":{amount},\"leverage\":1,\"price\":\"{bestRealAsk}\",\"priceProtect\":\"0\"}}";
            var sign = GetMd5Digest(dateNow + query + g);

            client.DefaultRequestHeaders.Add("x-mxc-sign", sign);
            client.DefaultRequestHeaders.Add("x-mxc-nonce", dateNow);
            client.DefaultRequestHeaders.Add("Authorization", u_id);
            var content = new StringContent(query, Encoding.UTF8, "application/json");

            // Console.WriteLine($"x-mxc-sign: {sign}, x-mxc-nonce: {dateNow}, Authorization: {u_id}");

            var orderUrl = @"https://futures.mexc.com/api/v1/private/order/create";
            var response = client.PostAsync(orderUrl, content).Result;

            Console.WriteLine(response);
            Console.WriteLine(response.Content.ReadAsStringAsync().Result); */
        }

        public override void GetEarnProducts(List<EarnProduct> products, decimal minApr)
        {
            Console.WriteLine($"{Exchange.Mexc.Name} - Page...");

            try
            {
                var earnString = DownloadWithCurl("get-mexc-earn-products.bat");

                /* var client = CreateMexcClient();
                client.DefaultRequestHeaders.Add("Accept", @"text/html,application/xhtml+xml,application/xml;q=0.9,*//*;q=0.8"); // escape slash

                var earnString = client.GetStringAsync("https://www.mexc.com/api/financialactivity/financial/products/list/V2").Result;
                // var earnString = client.GetStringAsync("https://www.mexc.com/api/operateactivity/staking").Result; */
                dynamic? earnData = JsonConvert.DeserializeObject(earnString.Trim()) ?? throw new Exception("mexc earn returned no data");
                foreach (var coin in earnData.data)
                {
                    if (coin.financialProductList == null) continue; // EFTD - new user
                    // if ((coin.lockPosList == null) || (coin.lockPosList[0].joinConditions == "EFTD")) continue; // EFTD - new user
                    foreach (var offer in coin.financialProductList)
                    {
                        if ((offer.showApr == null) || (offer.memberType == "EFTD")) continue; // EFTD - new user
                        if (offer.sort == 3260107) continue; // VIP?
                        decimal apr = offer.showApr;
                        if (apr < minApr) continue;
                        string currency = coin.currency; // explicite type

                        // Console.WriteLine($"{coin.currency}: {apr}");
                        // Console.WriteLine($"{coin.currency}: {offer.profitRate * 100M}%");
                        var product = new EarnProduct(Exchange.Mexc, currency, apr, "from page")
                        {
                            StableCoin = StableCoin.USDT,
                            SpotFee = 0
                        };
                        if (offer.perPledgeMaxQuantity != null) product.LimitMax = offer.perPledgeMaxQuantity;
                        if (offer.fixedInvestPeriodCount == null) product.Term = 1;
                        else product.Term = offer.fixedInvestPeriodCount;
                        products.Add(product);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while process mexc earn products: " + ex.Message);
            }
        }

        public override void EnterShort(string coin, decimal amount, string stableCoin = EmptyString)
        {
            EnterShortWeb(coin, amount);

            /* var bestRealAsk = 0.07M;
            var symbol = "PLAY_USDT";
            amount = 150;*/

            /*var bestRealAsk = 150000M;
            var symbol = "BTC_USDT";
            amount = 10;

            Console.WriteLine($"placing short order: {symbol}, {bestRealAsk} x {amount}...");
            var orderResult = _apiClient.FuturesApi.Trading.PlaceOrderAsync(symbol, bestRealAsk, amount, 1, 3, 2, 2).Result;
            // var orderResult = _apiClient.FuturesApi.Trading.PlaceOrderAsync("AGT_USDT", 0.009318M, 0, 1, 3, 2, 2).Result;
            // var orderResult = _apiClient.FuturesApi.Trading.PlaceOrderAsync("AGT_USDT", 0.009318M, 10000, 1, 3, 2, 2).Result;
            if (orderResult.Data.Success) Console.WriteLine($"{orderResult.Data.Data}");
            else Console.WriteLine($"Order error: {GetErrorText(orderResult.Data.Code)} ({orderResult.Data.Code})"); */

            // ScanFutures(coin, amount);
        }

        public override decimal GetSpotBalance(string? coin = null)
        {
            coin ??= StableCoin.USDT;
            var accountData = _apiClient.SpotApi.Account.GetAccountInfoAsync().Result;
            if (accountData == null) throw new Exception($"{Name} returned no data for spot");
            if (!accountData.Success) throw new Exception(accountData.Error == null ? "no data" : accountData.Error.Message);
            // var rest = 0M;
            foreach (var balance in accountData.Data.Balances)
            {
                // Console.WriteLine($"   Spot {balance.Asset}: Free: {balance.Available}, Locked: {balance.Locked}");
                if (coin.Equals(balance.Asset, StringComparison.CurrentCultureIgnoreCase)) return balance.Available; // return 
            }
            return 0;
        }

        public override decimal GetFuturesBalance(string? coin = null)
        {
            var result = _apiClient.FuturesApi.Account.GetAccountInfoAsync().Result;
            if (!result.Success && (result.Error != null)) throw new Exception(result.Error.Message);
            if (result.Data.Data == null) return 0;

            foreach (var c in result.Data.Data)
            {
                if (c.Currency == coin)
                {
                    // Console.WriteLine($"{c.Currency}. total: {c.Equity}, free: {c.AvailableBalance}");
                    return c.AvailableBalance;
                }
            }
            return 0;
        }

        private MexcPrice[]? _prices = null;

        private MexcContractInfo? _contractInfo = null;

        // private UpdateSubscription? _orderBookSubscription = null;

        public override decimal GetSpotPrice(string coin, string stablecoin = EmptyString)
        {
            if (_prices == null)
            {
                var tickerInfo = _apiClient.SpotApi.ExchangeData.GetPricesAsync().Result; // [coin + UsdtName]
                if (!tickerInfo.Success) throw new Exception(tickerInfo.Error == null ? string.Empty : tickerInfo.Error.Message);
                _prices = tickerInfo.Data;
            }
            var ourSymbol = _prices.FirstOrDefault(s => s.Symbol == coin.ToUpper() + StableCoin.USDT);
            // if (tickerInfo.Data.Length == 0) throw new Exception($"{Name} returned no data for {coin}");
            if (ourSymbol == null) return 0;
            else return ourSymbol.Price;
        }

        public override decimal FindFunds(string coin, bool forSpot = true, decimal amount = 0)
        {
            coin ??= StableCoin.USDT;
            var futuresRest = GetFuturesBalance(coin);
            Console.WriteLine($"   Futures rest: {futuresRest}");
            Console.WriteLine($"   Earn rest: Not available via Api");

            /*var assets = _apiClient.SpotApi.Account.GetUserAssetsAsync().Result;
            if (assets.Success)
            {
                foreach (var a in assets.Data)
                {
                    Console.WriteLine($"   contract rest: {a.AssetName}: {a.Asset}");
                }
            }*/

            return futuresRest;
        }

        private MexcSymbol? _spotSymbolInfo = null;

        public override decimal GetMaxLimit(string coin, bool isSpot, string stablecoin = EmptyString)
        {
            if (isSpot)
            {
                var exchangeResult = _apiClient.SpotApi.ExchangeData.GetExchangeInfoAsync([coin + StableCoin.USDT]).Result;
                if (!exchangeResult.Success) throw new Exception(exchangeResult.Error == null ? string.Empty : exchangeResult.Error.Message);
                if (exchangeResult.Data.Symbols.Length == 0) throw new Exception($"{Name} returned no max limit for {coin}");
                _spotSymbolInfo = exchangeResult.Data.Symbols[0];
                return _spotSymbolInfo.MaxQuoteQuantity;
            }
            else
            {
                if (_contractInfo == null)
                {
                    var contractInfo = _apiClient.FuturesApi.ExchangeData.GetContractInformationAsync(coin + '_' + StableCoin.USDT).Result;
                    if (!contractInfo.Success) throw new Exception(contractInfo.Error == null ? string.Empty : contractInfo.Error.Message);
                    if (contractInfo.Data.Data == null) return 0;
                    _contractInfo = contractInfo.Data.Data;
                }
                return _contractInfo.MaxVol * _contractInfo.СontractSize;

            }
        }

        public override decimal GetMinLimit(string coin, bool isSpot, string stablecoin = EmptyString)
        {
            if (!isSpot) throw new NotImplementedException();
            if (_spotSymbolInfo == null) throw new Exception($"{Name} has no {coin} information");
            // return _spotSymbolInfo.BaseAssetPrecision;
            return _spotSymbolInfo.QuoteQuantityPrecision;
        }

        public override void GetFundingRates(List<FundingRate> rates, decimal minRate)
        {
            var ratesData = _apiClient.FuturesApi.ExchangeData.GetFundingRatesAsync().Result;
            if (!ratesData.Success && (ratesData.Error != null)) throw new Exception(ratesData.Error.Message);
            if (ratesData.Data.Data == null) throw new Exception("Mexc returned no funding rates");
            foreach (var s in ratesData.Data.Data)
            {
                if ((s == null) || (s.Symbol == null)) continue;
                if (!s.Symbol.EndsWith(StableCoin.USDT) || (s.FundingRate <= minRate / 100)) continue;
                var fr = new FundingRate(this, s.Symbol, s.FundingRate * 100 ?? 0);
                fr.Interval = s.CollectCycle ?? 0;
                if (fr.Interval == 4) fr.CurrRate *= 2;
                rates.Add(fr);
            }
        }

        public override decimal GetOrderBookTicker(string coin, bool isSpot, bool isAsk)
        {
            // Console.WriteLine($"mexc: GetOrderBookTicker");
            WebCallResult<decimal?> priceInfo;
            if (isSpot)
            {
                var spotTicker = _apiClient.SpotApi.ExchangeData.GetBookPricesAsync(coin + StableCoin.USDT).Result;
                // Console.WriteLine($"mexc: {spotTicker}");
                decimal? bestSpotPrice = 0;
                if (spotTicker.Data != null) bestSpotPrice = isAsk ? spotTicker.Data.BestAskPrice : spotTicker.Data.BestBidPrice;
                priceInfo = spotTicker.As(bestSpotPrice);
            }
            else
            {
                var futuresTicker = _apiClient.FuturesApi.ExchangeData.GetOrderBookAsync(coin + '_' + StableCoin.USDT).Result;
                decimal? bestFuturesPrice = 0;
                // return 0;
                if (futuresTicker.Data.Data != null) bestFuturesPrice = isAsk ? futuresTicker.Data.Data.Asks[0].Price : futuresTicker.Data.Data.Bids[0].Price;
                priceInfo = futuresTicker.As(bestFuturesPrice);
            }

            if ((priceInfo.Error != null) && !priceInfo.Success)
            {
                // if (priceInfo.Error.Code == -1121) return 0;
                throw new Exception($"{Name} best spot price returned error: {priceInfo.Error.Message} / {priceInfo.Error.Code}");
            }
            return priceInfo.Data ?? 0;
        }

        private async void SubscribeUserSpotData()
        {
            var listenKeyResult = _apiClient.SpotApi.Account.StartUserStreamAsync().Result;
            if (!listenKeyResult.Success) throw new Exception($"Error while getting listenKey: {listenKeyResult.Error}");
            var listenKey = listenKeyResult.Data;
            Console.WriteLine($"mexc listenKey: {listenKey}");

            var subscribeResult = await _socketClient.SpotApi.SubscribeToOrderUpdatesAsync(listenKey, data =>
            {
                Console.WriteLine($"Order updated: {data.Data}, ID: {data.Data.OrderId}, Status: {data.Data.Status}");
                Console.Beep();
            });
        }

        // private string _orderId = string.Empty;
        // private decimal _orderPrice = -1;
        private MexcOrder? _order = null;

        private MexcOrder PlaceOrder(string symbol, decimal amount, decimal price)
        {
            Console.WriteLine($"Placing spot buy order: {symbol}, {price} x {amount}...");
            // WebCallResult orderResult;
            if (IsTest)
            {
                var testOrderResult = _apiClient.SpotApi.Trading.PlaceTestOrderAsync(symbol, OrderSide.Buy, OrderType.LimitMaker, amount, null, price).Result;
                if (!testOrderResult.Success) throw new Exception($"Error while placing {Name} spot order: {testOrderResult.Error}");
                Console.WriteLine($"Test order is placed");
                return new MexcOrder()
                {
                    Symbol = symbol,
                    Price = price,
                    Quantity = amount
                };
            }
            else
            {
                var orderResult = _apiClient.SpotApi.Trading.PlaceOrderAsync(symbol, OrderSide.Buy, OrderType.LimitMaker, amount, null, price).Result;
                if (!orderResult.Success) throw new Exception($"Error while placing {Name} spot order: {orderResult.Error}");
                Console.WriteLine($"Order Status: {orderResult.Data.Status}; id: {orderResult.Data.OrderId}");
                return orderResult.Data;
            }
        }

        public override void BuySpot(string coin, decimal amount)
        {
            SubscribeUserSpotData();

            var symbol = coin + '_' + StableCoin.USDT;
            _isLock = false;
            _order = null;
            _orderBookSubscription = _socketClient.SpotApi.SubscribeToBookTickerUpdatesAsync($"{symbol}", update =>
            {
                lock (Locker)
                {
                    if (_isLock) return;
                    if ((_order != null) && (_order.Price == update.Data.BestBidPrice)) return;
                    _isLock = true;
                }

                if (_order == null) _order = PlaceOrder(symbol, amount, update.Data.BestBidPrice);
                else
                {
                    if (update.Data.BestBidPrice > _order.Price)
                    { 
                        if (IsTest) Console.WriteLine($"Price incresed {update.Data.BestBidPrice}, the order should be cancelled");
                        else
                        {
                            var cancelResult = _apiClient.SpotApi.Trading.CancelOrderAsync(symbol, _order.OrderId).Result;
                            if (!cancelResult.Success) throw new Exception($"Error while cancelling the spot order: {cancelResult.Error}");
                            Console.WriteLine($"Price incresed {update.Data.BestBidPrice}, the order is cancelled ({cancelResult.Data.Status})");
                        }
                        _order = PlaceOrder(symbol, amount, update.Data.BestBidPrice);
                    }

                    if (update.Data.BestBidPrice < _order.Price)
                    {
                        Console.WriteLine($"Price dropped ({update.Data.BestBidPrice}), it seems the order is filled ({_order})");
                        if (_orderBookSubscription != null)  _ = _socketClient.UnsubscribeAsync(_orderBookSubscription);
                    }
                };

                _isLock = false;
            }).Result.Data;
        }

        protected override Order PlaceFuturesOrder(string symbol, decimal amount, decimal price) => throw new NotImplementedException();

        protected override Order CancelFuturesOrder(Order order) => throw new NotImplementedException();

        protected override void UnsubscribeOrderBookData() => throw new NotImplementedException();

        protected override void SubscribeOrderBookData(string symbol) => throw new NotImplementedException();
    }
}