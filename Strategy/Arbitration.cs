// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using DbSpace;
using Binance.Common;
using Binance.Spot;
using Binance.Spot.Models;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;

namespace Bnncmd
{
    internal class Rate
    {
        public string BaseAsset { get; set; }
        public string QuoteAsset { get; set; }
        public decimal BestBid { get; set; } = 0;
        public decimal BestAsk { get; set; } = 0;

        public decimal BidDensity { get; set; } = 0;
        public DateTime DensityTime { get; set; } = DateTime.MaxValue;

        public decimal Fee { get; set; }
        public decimal Profit { get; set; } = 0;
        public bool IsBuyer { get; set; }
        public MarketDataWebSocket MarketDataSocket { get; set; }

        public Rate(string baseAsset, string quoteAsset, decimal fee)
        {
            BaseAsset = baseAsset;
            QuoteAsset = quoteAsset;
            // rate.MarketDataSocket = new MarketDataWebSocket($"{BaseAsset}{QuoteAsset}@aggTrade");
            MarketDataSocket = new MarketDataWebSocket($"{baseAsset.ToLower()}{quoteAsset.ToLower()}@depth20@100ms");
            Fee = fee;
        }

        public override string ToString()
        {
            return $"{BaseAsset}{QuoteAsset} {BestBid:0.#######}";
        }
    }


    internal class Arbitration : BaseStrategy
    {
        private readonly string _mainAsset = "FDUSD";

        public Arbitration(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            BnnUtils.Log($"{GetName()} {SymbolName}");
            _dealParams = new DummyParams();
        }

        private readonly List<Rate> _rates = new();


        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            stopLossValue = 0;
            return double.MaxValue;
        }


        public override string GetName() { return "Arbitration"; }


        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            return 0;
        }


        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            return double.MaxValue;
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            // override abstract method
        }


        public override void Prepare()
        {
            //AddCurrencySet("TRX", "BTC"); - FDUSD not supported
            //AddCurrencySet("TRX", "ETH");
            //AddCurrencySet("TRX", "BNB");
            AddCurrencySet("ETH", "BTC");
            AddCurrencySet("BNB", "BTC");
            AddCurrencySet("BNB", "ETH");
            AddCurrencySet("XRP", "BTC");
            AddCurrencySet("XRP", "ETH");
            AddCurrencySet("XRP", "BNB");
            AddCurrencySet("DOGE", "BTC");
            AddCurrencySet("SOL", "BTC");
            AddCurrencySet("SOL", "ETH");
            AddCurrencySet("SOL", "BNB");
            AddCurrencySet("TON", "BTC");
            AddCurrencySet("ADA", "BTC");
            AddCurrencySet("ADA", "ETH");
            AddCurrencySet("ADA", "BNB");
            AddCurrencySet("LINK", "BTC");
            AddCurrencySet("LINK", "ETH");
            AddCurrencySet("LINK", "BNB");
            AddCurrencySet("DOT", "BTC");
            AddCurrencySet("DOT", "ETH");
            AddCurrencySet("DOT", "BNB");
            AddCurrencySet("BCH", "BTC");
            AddCurrencySet("BCH", "BNB");
            AddCurrencySet("SUI", "BTC");
            AddCurrencySet("SUI", "BNB");
            AddCurrencySet("LTC", "BTC");
            AddCurrencySet("LTC", "ETH");
            AddCurrencySet("LTC", "BNB");
            AddCurrencySet("UNI", "BTC");
            AddCurrencySet("UNI", "ETH");
            AddCurrencySet("NEAR", "BTC");
            AddCurrencySet("NEAR", "ETH");
            AddCurrencySet("NEAR", "BNB");
            AddCurrencySet("APT", "BTC");
            AddCurrencySet("APT", "ETH");
            AddCurrencySet("TAO", "BTC");
            AddCurrencySet("ICP", "BTC");
            AddCurrencySet("ICP", "ETH");
            AddCurrencySet("FET", "BTC");
            AddCurrencySet("FET", "BNB");
            AddCurrencySet("ETC", "BTC");
            AddCurrencySet("ETC", "ETH");
            AddCurrencySet("ETC", "BNB");
            AddCurrencySet("POL", "BTC");
            AddCurrencySet("POL", "ETH");
            AddCurrencySet("POL", "BNB");
            AddCurrencySet("AAVE", "ETH");
            AddCurrencySet("AAVE", "BTC");
            AddCurrencySet("RENDER", "BTC");
            AddCurrencySet("STX", "BTC");
            AddCurrencySet("STX", "BNB");
            AddCurrencySet("WIF", "BTC");
            AddCurrencySet("ARB", "BTC");
            AddCurrencySet("ARB", "ETH");
            AddCurrencySet("FIL", "BTC");
            AddCurrencySet("INJ", "BTC");
            AddCurrencySet("INJ", "ETH");
            AddCurrencySet("INJ", "BNB");
            AddCurrencySet("TIA", "BTC");
            AddCurrencySet("FTM", "BTC");
            AddCurrencySet("FTM", "ETH");
            AddCurrencySet("FTM", "BNB");
            AddCurrencySet("OP", "BTC");
            AddCurrencySet("OP", "ETH");
            AddCurrencySet("RUNE", "BTC");
            AddCurrencySet("RUNE", "ETH");
            AddCurrencySet("RUNE", "BNB");
            AddCurrencySet("ATOM", "BTC");
            AddCurrencySet("ATOM", "ETH");
        }


        private void AddRate(string baseAsset, string quoteAsset, decimal fee) //  --- async Task< async Task<Rate> async
        {
            // foreach (var r in _rates)
            for (var i = 0; i < _rates.Count; i++)
            {
                if ((_rates[i].BaseAsset == baseAsset) && (_rates[i].QuoteAsset == quoteAsset)) return; //  r;
            };

            // Console.WriteLine($"AddRate: {baseAsset} {quoteAsset}");

            var rate = new Rate(baseAsset, quoteAsset, fee);
            _rates.Add(rate);
            rate.MarketDataSocket.OnMessageReceived(data =>
            {
                // if (_isBusy) return Task.CompletedTask;
                dynamic? ordersData = JsonConvert.DeserializeObject(data.Trim()) ?? throw new Exception("aggTrade returned no data");
                rate.BestBid = ordersData.bids[0][0];
                rate.BestAsk = ordersData.asks[0][0];
                CurrencyPriceChanged();
                return Task.CompletedTask;
            }, CancellationToken.None);

            rate.MarketDataSocket.ConnectAsync(CancellationToken.None); // await 
                                                                        // Console.WriteLine($"AddRate Count: {_rates.Count}");			
            return; // rate;
        }


        private void AddCurrencySet(string baseAsset, string quoteAsset)
        {
            AddRate(quoteAsset, _mainAsset, 0); // await 
            AddRate(baseAsset, _mainAsset, 0);
            AddRate(baseAsset, quoteAsset, 0.00075M);
        }


        public override void Start()
        {
            _isBusy = false;
        }

        private readonly Market _market = new();

        private (decimal, decimal) Order(Rate rate, Side side, decimal quote)
        {
            var rateSymbol = rate.BaseAsset + rate.QuoteAsset;
            var price = side == Side.BUY ? rate.BestAsk : rate.BestBid;
            var quantity = side == Side.BUY ? FormatQuantity(quote / price) : quote; // quote - usdt

            // tempolary hack to escape LOT_SIZE error --- LimitOrder (DOGEBTC, sum=3545.977011, price=0.00000348): One or more errors occurred. ({"code":-1013,"msg":"Filter failure: LOT_SIZE"})
            // https://api.binance.com/api/v3/exchangeInfo?permissions=SPOT
            if (quantity > 10) quantity = Math.Floor(quantity);

            BnnUtils.Log($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} quantity = {quantity}, side = {side},  price = {price}, quote = {quote}");
            // var orderID = _manager.LimitOrder(rateSymbol, side, quantity, price);
            var orders = _market.OrderBook(rate.BaseAsset + rate.QuoteAsset, 1).Result;
            dynamic orderBookData = JsonConvert.DeserializeObject(orders.Trim()) ?? throw new Exception("depth returned no data");
            var realPrice = side == Side.BUY ? orderBookData.asks[0][0] : orderBookData.bids[0][0];
            BnnUtils.Log($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} {rateSymbol} current price: [{orderBookData.asks[0][0]:0.#####} / {orderBookData.bids[0][0]:0.#####}] => {realPrice} x {quantity}");
            if (side == Side.SELL)
            {
                quantity = quote * price;
                if (quantity > 10) quantity = Math.Floor(quantity);
            }
            return (quantity, realPrice);
        }

        private bool _isBusy = true;
        private decimal _totalProfit = 0;

        private void MakeDeal(Rate rate)
        {
            if (_isBusy) return;
            _isBusy = true;
            BnnUtils.Log($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} make deal {rate}");
            // Console.Beep();
            decimal realProfit = 1M;
            try
            {
                decimal sum = Math.Floor(_manager.SpotAmount);
                (decimal, decimal) orderResult;

                if (rate.IsBuyer)
                {
                    orderResult = Order(GetCurrency(rate.QuoteAsset), Side.BUY, sum);
                    realProfit = realProfit / orderResult.Item2;
                    orderResult = Order(rate, Side.BUY, orderResult.Item1); // buyedSum precision???
                    realProfit = realProfit / orderResult.Item2;
                    orderResult = Order(GetCurrency(rate.BaseAsset), Side.SELL, orderResult.Item1); // buyedSum ???
                    realProfit = realProfit * orderResult.Item2;
                }
                else
                {
                    orderResult = Order(GetCurrency(rate.BaseAsset), Side.BUY, sum);
                    realProfit = realProfit / orderResult.Item2;
                    orderResult = Order(rate, Side.SELL, orderResult.Item1);
                    realProfit = realProfit * orderResult.Item2;
                    orderResult = Order(GetCurrency(rate.QuoteAsset), Side.SELL, orderResult.Item1);
                    realProfit = realProfit * orderResult.Item2;
                };
                _totalProfit = _totalProfit + realProfit - 1;
            }
            finally
            {
                _isBusy = false;
            }
            BnnUtils.Log($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} real profit = {realProfit:0.#####}, total profit = {_totalProfit:0.#####}");
            BnnUtils.Log(string.Empty);
        }


        private Rate GetCurrency(string currName)
        {
            // foreach (var r in _rates)
            for (var i = 0; i < _rates.Count; i++)
            {
                if ((_rates[i].QuoteAsset == _mainAsset) && (_rates[i].BaseAsset == currName)) return _rates[i];
            };
            throw new Exception("Rate not found ( " + currName + " )");
            // return null;
        }


        private string _arbitrageInfo = string.Empty;

        private void CurrencyPriceChanged() // , decimal newPrice -- Rate rate
        {
            if (_isBusy) return;

            // foreach (var r in _rates)
            for (var i = 0; i < _rates.Count; i++)
            {
                var r = _rates[i];
                if ((r.QuoteAsset == _mainAsset) || (r.BestAsk == 0)) continue;

                var baseRate = GetCurrency(r.BaseAsset);
                var quoteRate = GetCurrency(r.QuoteAsset);
                if ((quoteRate.BestAsk == 0) || (baseRate.BestAsk == 0)) continue;
                //var profitQuote = baseRate.BestBid / GetCurrency(r.QuoteAsset).BestAsk / r.BestAsk * (1 - r.Fee); // FDUSDT - BTC - ETH - FDUSDT ( BTCFDUSD - ETHBTC - ETHFDUDS )

                var profitQuote = baseRate.BestBid / quoteRate.BestAsk / r.BestAsk * (1 - r.Fee); // FDUSDT - BTC - ETH - FDUSDT ( BTCFDUSD - ETHBTC - ETHFDUDS )
                var profitBase = r.BestBid * quoteRate.BestBid / baseRate.BestAsk * (1 - r.Fee); // FDUSDT - ETH - BTC - FDUSDT ( BTCFDUSD - ETHBTC - ETHFDUDS )
                if ((profitQuote == 0) || (profitBase == 0)) r.Profit = 0; //  || (profitQuote == decimal.NaN)
                else
                {
                    r.Profit = Math.Max(profitQuote, profitBase);
                    r.IsBuyer = profitQuote > 1;
                }
            }

            List<Rate> sortedRates = [.. _rates.OrderByDescending(r => r.Profit)];
            int counter = 0;
            foreach (var r in sortedRates)
            {
                if ((r.Profit < 1.0001M)) continue; //  || (r.Profit > 10  || (r.Profit == double.NaN)
                MakeDeal(r);

                if (_arbitrageInfo.Length > 150) continue;
                counter++;
                _arbitrageInfo += $"{r.BaseAsset}{r.QuoteAsset} {r.Profit:0.#####}   {r} {GetCurrency(r.BaseAsset)} {GetCurrency(r.QuoteAsset)}    ";
            }
            if (counter > 0) _arbitrageInfo += $" ({counter} items)   ";
        }


        public override string GetCurrentInfo()
        {
            if (_isBusy) return "Initializing...";
            var ratesInfo = string.Empty;
            for (var i = 0; i < _rates.Count; i++)
            {
                if (ratesInfo.Length < 201) ratesInfo += $"{_rates[i]}  "; // ###  --- 201
            }

            if (_arbitrageInfo != string.Empty)
            {
                BnnUtils.Log($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} {_arbitrageInfo}");
                _arbitrageInfo = string.Empty;
            }
            return ratesInfo + "...";
        }
    }
}