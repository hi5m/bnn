// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Objects.Models;
using Binance.Net.Objects.Models.Futures;
using Binance.Net.Objects.Models.Spot;
using Binance.Net.Objects.Options;
using Bnncmd.Strategy;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using CryptoExchange.Net.Objects.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic;
using Org.BouncyCastle.Asn1.X509;
using System.Runtime.CompilerServices;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static System.Windows.Forms.AxHost;

namespace Bnncmd
{
    internal class BinanceExchange : AbstractExchange
    {
        public BinanceExchange() : base()
        {
            var ak = AccountManager.Config.GetValue<string>("TKBNN") ?? string.Empty;
            var asc = AccountManager.Config.GetValue<string>("TSBNN") ?? string.Empty;
            var options = Options.Create(new BinanceRestOptions
            {
                OutputOriginalData = true,
                ApiCredentials = new ApiCredentials(ak, asc)
            });
            // var httpClient = BnnUtils.BuildLoggingClient();
            _apiClient = new BinanceRestClient(null, null, options); 
        }

        // private readonly string _fdusdtName = "FDUSD";
        public override string Name { get; } = "Binance";
        public override int Code { get; } = 0;
        // public override decimal SpotTakerFee { get; } = 0.1M;
        // public override decimal SpotMakerFee { get; } = 0; // FDUSD
        public override decimal FuturesTakerFee { get; } = 0.045M;
        public override decimal FuturesMakerFee { get; } = 0.018M; // USDT

        private readonly BinanceRestClient _apiClient;

        private readonly BinanceSocketClient _socketClient = new();

        private string GetChipSymbol(string coin, out decimal fee)
        {
            fee = 0; // fdusd
            if (_spotSymbols == null) throw new Exception("Coins sotre not found");
            var symbolInfo = _spotSymbols.FirstOrDefault(s => s.Name.Equals($"{coin}{StableCoin.FDUSD}", StringComparison.OrdinalIgnoreCase));
            if ((symbolInfo != null) && (symbolInfo.Status == SymbolStatus.Trading)) return StableCoin.FDUSD;

            fee = 0.075M; // usdt
            symbolInfo = _spotSymbols.FirstOrDefault(s => s.Name.Equals($"{coin}{StableCoin.USDT}", StringComparison.OrdinalIgnoreCase));
            if ((symbolInfo != null) && (symbolInfo.Status == SymbolStatus.Trading)) return StableCoin.USDT;
            return StableCoin.None;
        }

        private void GetLockedProducts(List<EarnProduct> products, decimal minApr)
        {
            Console.WriteLine($"{Exchange.Binance.Name} - Api - Locked...");
            WebCallResult<BinanceQueryRecords<Binance.Net.Objects.Models.Spot.SimpleEarn.BinanceSimpleEarnLockedProduct>> fixedProducts;
            var pageSize = 100;
            var pageNum = 1;

            do
            {
                try
                {
                    fixedProducts = _apiClient.GeneralApi.SimpleEarn.GetLockedProductsAsync(null, pageNum, pageSize).Result;
                    if (!fixedProducts.Success && (fixedProducts.Error != null))
                    {
                        if (fixedProducts.Error.Code == -2015) Console.WriteLine($"{BnnUtils.GetPublicIp()}"); // ip: 
                        throw new Exception(fixedProducts.Error.Message + " ( " + fixedProducts.Error.Code + " )"); //  ? $"{Name} throw exception while get data" : 
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.Message}");
                    Environment.Exit(0);
                    return;
                }

                if (fixedProducts.Data == null) throw new Exception("fixed product returned no data");
                foreach (var r in fixedProducts.Data.Rows)
                {
                    var apr = r.Details.Apr * 100;
                    if (apr > minApr)
                    {
                        var stable = GetChipSymbol(r.Details.Asset, out decimal fee);
                        var lockedProduct = new EarnProduct(Exchange.Binance, r.Details.Asset, apr, "locked - api")
                        {
                            Term = r.Details.Duration,
                            StableCoin = stable,
                            SpotFee = fee
                        };
                        products.Add(lockedProduct);
                    }
                }
                pageNum++;
            }
            while (fixedProducts.Data.Rows.Length == pageSize);
        }

        private void GetFlexibleProducts(List<EarnProduct> products, decimal minApr)
        {
            Console.WriteLine($"{Exchange.Binance.Name} - Api - Flexible...");
            WebCallResult<BinanceQueryRecords<Binance.Net.Objects.Models.Spot.SimpleEarn.BinanceSimpleEarnFlexibleProduct>>? flexibleProducts;
            var pageSize = 100;
            var pageNum = 1;

            do
            {
                flexibleProducts = _apiClient.GeneralApi.SimpleEarn.GetFlexibleProductsAsync(null, pageNum, pageSize).Result;
                if (!flexibleProducts.Success) throw new Exception(flexibleProducts.Error == null ? $"{Name} throw exception while get data" : flexibleProducts.Error.Message);
                foreach (var r in flexibleProducts.Data.Rows)
                {
                    var apr = r.LatestAnnualPercentageRate * 100;
                    if (apr > minApr)
                    {
                        // Console.WriteLine($"{r.Asset}: {apr}%");
                        var stable = GetChipSymbol(r.Asset, out decimal fee);
                        var flexibleProduct = new EarnProduct(Exchange.Binance, r.Asset, apr, "flexible - api")
                        {
                            StableCoin = stable,
                            SpotFee = fee,
                        };
                        products.Add(flexibleProduct);
                    }
                }
                pageNum++;
            }
            while (flexibleProducts.Data.Rows.Length == pageSize);
        }

        public override void GetEarnProducts(List<EarnProduct> products, decimal minApr)
        {
            _spotSymbols = LoadSpotSymbols();
            GetLockedProducts(products, minApr);
            GetFlexibleProducts(products, minApr);
        }

        public override decimal GetSpotBalance(string? coin = null)
        {
            if (coin == null) coin = StableCoin.USDT;
            try
            {
                var accInfo = _apiClient.SpotApi.Account.GetAccountInfoAsync().Result;
                if ((accInfo.Error != null) && !accInfo.Success) throw new Exception(accInfo.Error.Message);
                if (accInfo.Data == null) throw new Exception("AccountInfo returned no data");

                foreach (var b in accInfo.Data.Balances)
                {
                    if (b.Asset.Equals(coin, StringComparison.OrdinalIgnoreCase)) return b.Available;
                    if ((b.Available == 0) || (b.Locked == 0)) continue;
                    Console.WriteLine($"{b.Asset}, Free: {b.Available}, Locked: {b.Locked}");
                }
            }
            catch (Exception ex)
            {
                // if (ex.Message.Contains("-2015")) Console.WriteLine(BnnUtils.GetPublicIp(), false); // {"code":-2015,"msg":"Invalid API-key, IP, or permissions for action."}	
                Console.WriteLine($"{ex.Message}");
            }
            return 0;
        }

        public override decimal GetFuturesBalance(string? coin = null)
        {
            var accInfo = _apiClient.UsdFuturesApi.Account.GetAccountInfoV3Async().Result;
            if ((accInfo.Error != null) && (!accInfo.Success)) throw new Exception(accInfo.Error.Message);
            return accInfo.Data.AvailableBalance;
        }

        public override decimal GetSpotPrice(string coin)
        {
            /*var priceInfo = _client.SpotApi.ExchangeData.GetPriceAsync(coin + UsdtName).Result;
            if ((priceInfo.Error != null) && !priceInfo.Success)
            {
                if (priceInfo.Error.Code == -1121) return 0;
                throw new Exception("Exception from Error: " + priceInfo.Error.Message);
            }
            Console.WriteLine($"\t\t{Name} {UsdtName} price: {priceInfo.Data.Price}");*/

            var priceInfo = _apiClient.SpotApi.ExchangeData.GetPriceAsync(coin + StableCoin.FDUSD).Result;
            if ((priceInfo.Error != null) && !priceInfo.Success)
            {
                if (priceInfo.Error.Code == -1121) return 0;
                throw new Exception(priceInfo.Error.Message);
            }
            return priceInfo.Data.Price;
        }

        private readonly string _usdtFlexibleDeposite = "USDT001";

        private decimal TranserFromSpotToFutures(decimal amount)
        {
            Console.WriteLine($"Tranfering from spot to futures wallet: {amount} ...");
            var transferRes = _apiClient.GeneralApi.Futures.TransferFuturesAccountAsync(StableCoin.USDT, amount, FuturesTransferType.FromSpotToUsdtFutures).Result;
            if ((transferRes.Error != null) && !transferRes.Success) throw new Exception(transferRes.Error.Message);
            var newBalance = GetFuturesBalance();
            Console.WriteLine($"New futures balance: {newBalance}");
            return newBalance;
        }

        private decimal TranserFromFuturesToSpot(decimal amount)
        {
            Console.WriteLine($"Tranfering from futures to spot wallet: {amount} ...");
            var transferRes = _apiClient.GeneralApi.Futures.TransferFuturesAccountAsync(StableCoin.USDT, amount, FuturesTransferType.FromUsdtFuturesToSpot).Result;
            if ((transferRes.Error != null) && !transferRes.Success) throw new Exception(transferRes.Error.Message);
            var newBalance = GetSpotBalance();
            Console.WriteLine($"New spot balance: {newBalance:0.###}");
            return newBalance;
        }

        public override decimal FindFunds(string stableCoin, bool forSpot = true, decimal amount = 0)
        {
            decimal sum = 0;

            if (forSpot)
            {
                if (stableCoin == string.Empty) stableCoin = StableCoin.FDUSD;
                var futuresRest = GetFuturesBalance(StableCoin.USDT);
                sum += futuresRest;
                if (amount > 0)
                {
                    if (stableCoin.Equals(StableCoin.USDT, StringComparison.OrdinalIgnoreCase)) return TranserFromFuturesToSpot(futuresRest > amount ? amount : futuresRest);
                    else throw new Exception($"Futures rest is in {futuresRest:0.###}{StableCoin.USDT}, but you probably want some {StableCoin.FDUSD}?");
                }
                else Console.WriteLine($"   Futures rest: {futuresRest}");
            }
            else
            {
                var spotRest = GetSpotBalance(StableCoin.USDT);
                sum += spotRest;
                if (amount > 0) return TranserFromSpotToFutures(spotRest > amount ? amount : spotRest);
                else Console.WriteLine($"   Spot rest: {spotRest}");
            }

            var flexiblePositions = _apiClient.GeneralApi.SimpleEarn.GetFlexibleProductPositionsAsync(null, _usdtFlexibleDeposite).Result;
            if ((flexiblePositions.Error != null) && !flexiblePositions.Success) throw new Exception(flexiblePositions.Error.Message);
            var earnRest = flexiblePositions.Data.Rows.Length == 0 ? 0 : flexiblePositions.Data.Rows.First().TotalQuantity;
            sum += earnRest;
            if ((amount > 0) && (earnRest >= amount))
            {
                Console.WriteLine($"Redeeming earn rest to spot wallet: {amount} ...");
                // var toWallet = forSpot ? AccountSource.Spot : AccountSource.;
                var redeemRes = _apiClient.GeneralApi.SimpleEarn.RedeemFlexibleProductAsync(_usdtFlexibleDeposite, false, amount, AccountSource.Spot).Result;
                if ((redeemRes.Error != null) && !redeemRes.Success) throw new Exception(redeemRes.Error.Message);
                Console.WriteLine($"New spot balance: {GetSpotBalance(StableCoin.USDT)}");
                if (!forSpot) return TranserFromSpotToFutures(amount);
                return amount;
            }
            else Console.WriteLine($"   Earn rest: {earnRest}");

            return sum;
        }

        private BinanceFuturesUsdtSymbol? _symbolFuturesInfo = null;

        public override decimal GetMinLimit(string coin, bool isSpot, string stablecoin = EmptyString)
        {
            var symbol = coin + (stablecoin == EmptyString ? StableCoin.USDT : stablecoin);
            if (isSpot)
            {
                _spotSymbols = LoadSpotSymbols();
                var symbolInfo = _spotSymbols.FirstOrDefault(s => s.Name.Equals(symbol, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"MinLimit: {symbol} info not found on {Name}");
                return symbolInfo.LotSizeFilter == null ? 0 : symbolInfo.LotSizeFilter.MinQuantity;
            }
            else
            {
                if ((_symbolFuturesInfo == null) || (_symbolFuturesInfo.LotSizeFilter == null)) throw new Exception($"MinLimit: {Name} has no {symbol} information");
                return _symbolFuturesInfo.LotSizeFilter.MinQuantity; //  * GetSpotPrice(coin)
            }
        }

        private BinanceSymbol[]? _spotSymbols = null;

        private BinanceSymbol[] LoadSpotSymbols()
        {
            if (_spotSymbols != null) return _spotSymbols;
            var exchangeInfoResult = _apiClient.SpotApi.ExchangeData.GetExchangeInfoAsync().Result;
            if ((exchangeInfoResult.Error != null) && !exchangeInfoResult.Success) throw new Exception(exchangeInfoResult.Error.Message);
            _spotSymbols = exchangeInfoResult.Data.Symbols;
            return _spotSymbols;
        }

        public override decimal GetMaxLimit(string coin, bool isSpot, string stablecoin = EmptyString)
        {
            var symbol = coin + (stablecoin == EmptyString ? StableCoin.USDT : stablecoin);

            if (isSpot)
            {
                _spotSymbols = LoadSpotSymbols();
                var symbolInfo = _spotSymbols.FirstOrDefault(s => s.Name.Equals(symbol, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"{symbol} info not found on {Name}");
                return symbolInfo.LotSizeFilter == null ? 0 : symbolInfo.LotSizeFilter.MaxQuantity; //  * GetSpotPrice(coin)
            }
            else
            {
                var positionInfo = _apiClient.UsdFuturesApi.Account.GetPositionInformationAsync(symbol).Result.Data.First();
                if (positionInfo.Leverage != 1)
                {
                    Console.WriteLine($"{Name} current position size: {positionInfo.Quantity}, leverage: {positionInfo.Leverage}x");
                    var leverageResult = _apiClient.UsdFuturesApi.Account.ChangeInitialLeverageAsync(symbol, 1).Result;
                    if ((leverageResult.Error != null) && !leverageResult.Success) throw new Exception(leverageResult.Error.Message);
                    Console.WriteLine($"{Name} new leverage: {leverageResult.Data.Leverage}");
                };

                var exchData = _apiClient.UsdFuturesApi.ExchangeData.GetExchangeInfoAsync().Result;
                if ((exchData.Error != null) && !exchData.Success) throw new Exception(exchData.Error.Message);
                if ((exchData.Data.Symbols == null) || (exchData.Data.Symbols.Length == 0)) throw new Exception($"{Name} returned not data for {coin}");
                _symbolFuturesInfo = exchData.Data.Symbols.FirstOrDefault(s => s.Name == symbol) ?? throw new Exception($"{Name} returned not suitable symbol for {coin}");

                _priceStep = _symbolFuturesInfo.PriceFilter == null ? 0 : _symbolFuturesInfo.PriceFilter.TickSize;
                return _symbolFuturesInfo.LotSizeFilter == null ? 0 : _symbolFuturesInfo.LotSizeFilter.MaxQuantity; //  * GetSpotPrice(coin)
            }
        }

        public override void GetFundingRates(List<FundingRate> rates, decimal minRate)
        {
            var fundingInfo = _apiClient.UsdFuturesApi.ExchangeData.GetMarkPricesAsync().Result;
            if ((fundingInfo.Error != null) && !fundingInfo.Success) throw new Exception(fundingInfo.Error.Message);
            foreach (var s in fundingInfo.Data)
            {
                if (!s.Symbol.EndsWith(StableCoin.USDT) || (s.FundingRate <= minRate / 100)) continue;
                var fr = new FundingRate(this, s.Symbol, s.FundingRate * 100 ?? 0);
                rates.Add(fr);
            }
        }

        public void GetFundingRateStat(string symbol, int daysCount)
        {
            Console.WriteLine($" * {symbol.ToUpper()} statistics on {Name} for {daysCount} last days *");
            Console.WriteLine();

            // ema, avg, min, max, first date, last value, sum, interval, ...
            var fundingRates = _apiClient.UsdFuturesApi.ExchangeData.GetFundingRatesAsync(symbol, DateTime.Now.AddDays(-daysCount), DateTime.Now, 1000).Result.Data;
            if (fundingRates.Length < 2) throw new Exception($"There are no values for this symbol on {Name}");
            var firstDate = fundingRates.First().FundingTime;
            var lastValue = fundingRates.Last().FundingRate * 100;
            var interval = fundingRates[^1].FundingTime.Hour - fundingRates[^2].FundingTime.Hour;
            var minValue = decimal.MaxValue;
            var maxValue = decimal.MinValue;
            var minValueDay = DateTime.MinValue;
            var sum = 0M;
            var ema = fundingRates.First().FundingRate;
            var emaKoef = 0.3M;
            foreach (var f in fundingRates) // .Reverse()
            {
                if (f.FundingRate > maxValue) maxValue = f.FundingRate;
                if (f.FundingRate < minValue)
                {
                    minValue = f.FundingRate;
                    minValueDay = f.FundingTime;
                }
                sum += f.FundingRate;
                ema = ema * (1 - emaKoef) + emaKoef * f.FundingRate;
            }

            Console.WriteLine($"First date: {firstDate:dd.MM.yyyy}");
            Console.WriteLine($"Interval: {interval} hours");
            Console.WriteLine($"Last value: {lastValue:0.###}");
            Console.WriteLine($"Max value: {maxValue * 100:0.###}%");
            Console.WriteLine($"Min value: {minValue * 100:0.###}%");
            Console.WriteLine($"Min value day: {minValueDay:dd.MM.yyyy}");
            Console.WriteLine($"Sum: {sum * 100:0.###}%");
            Console.WriteLine($"Avg: {sum * 100 / (decimal)fundingRates.Length:0.#####}%");
            Console.WriteLine($"Ema: {ema * 100:0.###}%");
        }

        private void AddHedge(List<HedgeInfo> hedges, string symbol, decimal fee)
        {
            var fundingRates = _apiClient.UsdFuturesApi.ExchangeData.GetFundingRatesAsync(symbol, DateTime.Now.AddDays(-FundingRateDepth), DateTime.Now, 72).Result;
            if (fundingRates.Data.Length < 2) return;
            var fundingInterval = fundingRates.Data[^1].FundingTime.Hour - fundingRates.Data[^2].FundingTime.Hour;
            var ratesArr = fundingRates.Data.Select(r => r.FundingRate).Reverse().Take(10).ToArray(); // then process EMA
            hedges.Add(new HedgeInfo(this)
            {
                Symbol = symbol,
                EmaFundingRate = 100 * GetEmaFundingRate(ratesArr) * 24 / fundingInterval,
                Fee = fee
            });
        }

        public override HedgeInfo[] GetDayFundingRate(string coin)
        {
            var hedges = new List<HedgeInfo>();
            AddHedge(hedges, coin + StableCoin.USDT, FuturesMakerFee);
            AddHedge(hedges, coin + StableCoin.USDC, 0);
            return [.. hedges];

            /*var fundingInterval = fundingRates.Data[^1].FundingTime.Hour - fundingRates.Data[^2].FundingTime.Hour;
            var currFundingRate = fundingRates.Data[^1].FundingRate * 100 * 24 / fundingInterval;
            return currFundingRate;*/

            /* decimal sumFundingRate = 0;
            foreach (var r in fundingRates.Data)
            {
                Console.WriteLine($"      {r.FundingTime}: {r.FundingRate * 100}");
                sumFundingRate += r.FundingRate * 100;
            }*/
        }

        public override decimal GetOrderBookTicker(string coin, bool isSpot, bool isAsk)
        {
            var priceInfo = isSpot ? _apiClient.SpotApi.ExchangeData.GetBookPriceAsync(coin + StableCoin.FDUSD).Result : _apiClient.UsdFuturesApi.ExchangeData.GetBookPriceAsync(coin + StableCoin.USDT).Result;

            // Console.WriteLine($"binance: {priceInfo}");

            if ((priceInfo.Error != null) && !priceInfo.Success)
            {
                if (priceInfo.Error.Code == -1121) return 0;
                throw new Exception($"{Name} best price returned error: {priceInfo.Error.Message} / {priceInfo.Error.Code}");
            }
            return isAsk ? priceInfo.Data.BestAskPrice : priceInfo.Data.BestBidPrice;
        }

        public override void BuySpot(string coin, decimal amount) => throw new NotImplementedException();

        // Futures Routines
        private async void SubscribeUserFuturesData()
        {
            var listenKeyResult = _apiClient.UsdFuturesApi.Account.StartUserStreamAsync().Result;
            if (!listenKeyResult.Success) throw new Exception($"Error while getting listenKey: {listenKeyResult.Error}");
            var listenKey = listenKeyResult.Data;
            // Console.WriteLine($"listenKey: {listenKey}");

            var subscribeResult = await _socketClient.UsdFuturesApi.Account.SubscribeToUserDataUpdatesAsync(
                listenKey,
                /* onAccountUpdate: data =>
                {
                    Console.WriteLine("💼 Обновление аккаунта:");
                    foreach (var balance in data.Data.Balances)
                    {
                        Console.WriteLine($"- {balance.Asset}: свободно {balance.Free}, заблокировано {balance.Locked}");
                    }
                },*/
                onOrderUpdate: data => {
                    if (data.Data.UpdateData.Status == Binance.Net.Enums.OrderStatus.New) return;
                    Console.WriteLine($"Order updated: {data.Data.UpdateData.Symbol}, ID: {data.Data.UpdateData.OrderId}, Status: {data.Data.UpdateData.Status}");
                    Console.Beep();
                    FireShortEntered();
                }
            );

            // return listenKey;
        }

        public override void EnterShort(string coin, decimal amount, string stableCoin = EmptyString)
        {
            var symbol = coin + (stableCoin == string.Empty ? StableCoin.USDT : stableCoin);
            ScanFutures(symbol, amount);
        }

        protected override async void SubscribeOrderBookData(string symbol)
        {
            /*var positionResult = _apiClient.UsdFuturesApi.Account.GetPositionInformationAsync(symbol).Result;
            Console.WriteLine($"Position: {positionResult.Data.First()}");
            Console.WriteLine($"Leverage: {positionResult.Data.First().Leverage}");
            Console.WriteLine($"Notional: {positionResult.Data.First().Notional}");
            // return;*/

            SubscribeUserFuturesData();

            _orderBookSubscription = (await _socketClient.UsdFuturesApi.ExchangeData.SubscribeToPartialOrderBookUpdatesAsync(symbol, 20, 100, e =>
            {
                var asks = e.Data.Asks.Select(a => new[] { a.Price, a.Quantity }).ToArray();
                var bids = e.Data.Bids.Select(b => new[] { b.Price, b.Quantity }).ToArray();

                ProcessFuturesOrderBook(symbol, asks, bids);
            })).Data;
        }

        protected override async void UnsubscribeOrderBookData()
        {
            if (_orderBookSubscription == null) return;
            await _socketClient.UnsubscribeAsync(_orderBookSubscription);
            _orderBookSubscription = null;
        }

        protected override Order PlaceFuturesOrder(string symbol, decimal amount, decimal price)
        {
            if (IsTest)
            {
                return new Order()
                {
                    Id = $"test_order_{Name}",
                    Price = price,
                    Amount = amount
                };
            }
            else
            {
                var orderResult = _apiClient.UsdFuturesApi.Trading.PlaceOrderAsync(symbol, OrderSide.Sell, FuturesOrderType.Limit, amount, price, null, TimeInForce.GoodTillCanceled).Result;
                if (!orderResult.Success) throw new Exception($"Error while placing order: {orderResult.Error}");
                Console.WriteLine($"New {Name} futures order status: {orderResult.Data.Status}");
                return new Order()
                {
                    Id = orderResult.Data.Id.ToString(),
                    Price = orderResult.Data.Price,
                    Amount = orderResult.Data.Quantity
                };
                // return _futuresOrder;
            }
        }
    }
}