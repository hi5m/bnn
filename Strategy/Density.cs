// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Binance.Spot;
using Bnncmd;
using Newtonsoft.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Bnncmd.Strategy
{
    internal class Density : BaseStrategy
    {
        // private readonly string _mainAsset; // = "FDUSD";

        public Density(string symbolName, AccountManager manager) : base(symbolName, manager)
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


        public override string GetName() { return "Density"; }


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
            AddRate("BTC");
            AddRate("ETH");
            AddRate("BNB");
            AddRate("XRP");
            AddRate("SOL");

            AddRate("AAVE");
            AddRate("ADA");
            AddRate("ARB");
            AddRate("ATOM");
            AddRate("APT");
            AddRate("BCH");
            AddRate("DOGE");
            AddRate("DOT");
            AddRate("ETC");
            AddRate("FET");
            AddRate("FIL");
            AddRate("FTM");
            AddRate("ICP");
            AddRate("INJ");
            AddRate("LINK");
            AddRate("LTC");
            AddRate("NEAR");
            AddRate("OP");
            AddRate("POL");
            AddRate("RENDER");
            AddRate("RUNE");
            AddRate("STX");
            AddRate("SUI");
            AddRate("TIA");
            AddRate("TON");
            AddRate("TAO");
            AddRate("UNI");
            AddRate("WIF");
        }


        private void AddRate(string baseAsset, string quoteAsset = "FDUSD", decimal fee = 0) //  --- async Task< async Task<Rate> async
        {
            for (var i = 0; i < _rates.Count; i++)
            {
                if ((_rates[i].BaseAsset == baseAsset) && (_rates[i].QuoteAsset == quoteAsset)) return; //  r;
            };

            var rate = new Rate(baseAsset, quoteAsset, fee);
            _rates.Add(rate);
            rate.MarketDataSocket.OnMessageReceived(data =>
            {
                dynamic? ordersData = JsonConvert.DeserializeObject(data.Trim()) ?? throw new Exception("aggTrade returned no data");
                rate.BestBid = ordersData.bids[0][0];
                rate.BestAsk = ordersData.asks[0][0];

                decimal askSum = 0;
                for (int i = ordersData.asks.Count - 1; i >= 0; i--) // ordersData.asks.Count
                {
                    askSum += (decimal)ordersData.asks[i][1];
                }

                // var oldBid = rate.BidDensity;
                // rate.BidDensity = 0;
                decimal newDensityBid = 0;
                for (var i = 1; i < ordersData.bids.Count; i++) // ordersData.asks.Count
                {
                    if ((decimal)ordersData.bids[i][1] > askSum)
                    {
                        // rate.BidDensity = ordersData.bids[i][0];
                        newDensityBid = ordersData.bids[i][0];
                        break;
                    }
                }

                if (newDensityBid != rate.BidDensity) rate.DensityTime = DateTime.Now;
                rate.BidDensity = newDensityBid;

                // CurrencyPriceChanged();
                return Task.CompletedTask;
            }, CancellationToken.None);

            rate.MarketDataSocket.ConnectAsync(CancellationToken.None);
            return;
        }


        public override void Start()
        {
            _isBusy = false;
        }

        private readonly Market _market = new();

        private bool _isBusy = true;

        public override string GetCurrentInfo()
        {
            if (_isBusy) return "Initializing...";
            var ratesInfo = string.Empty;
            // var sortedRates = _rates; // .OrderBy(r => r.DensityTime).ToList();
            var sortedRates = _rates.OrderBy(r => r.DensityTime).ToList();
            // var sortedRates = _rates.OrderByDescending(r => r.DensityTime).ToList();
            for (var i = 0; i < sortedRates.Count; i++)
            {
                if ((ratesInfo.Length < 201) && (sortedRates[i].BidDensity > 0)) ratesInfo += $"{sortedRates[i]}/{sortedRates[i].BidDensity:0.#######}  "; // ###  --- 201
            }

            /* if (_arbitrageInfo != string.Empty)
            {
                BnnUtils.Log($"{DateTime.Now:dd.MM.yyyy HH:mm:ss} {_arbitrageInfo}");
                _arbitrageInfo = string.Empty;
            }*/
            return ratesInfo + "...";
        }
    }
}
