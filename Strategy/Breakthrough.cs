// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using DbSpace;
using Microsoft.Extensions.Configuration;

namespace Bnncmd
{
    internal class Breakthrough : TradeRangeBase
    {
        private static readonly int s_maInterval = 8;

        private static double _MA = 0;

        private readonly DealParams _bullParams;
        private readonly DealParams _bearParams;

        private readonly double _bearParamsValue = -0.1;
        private readonly double _bullParamsValue = 0.1;

        private readonly bool _isBullParams = true;
        private readonly bool _isBearParams = true;

        private double GetMA(long endTime, int length)
        {
            double result = -1;
            DB.OpenSingleQuery("select avg(ClosePrice) MA19 from candlestick1m c " +
                "inner join " +
                "(select max(OpenTime) CloseTime from candlestick1m " +
                $"where OpenTime > {endTime - length * 24 * 60 * 60 * 1000} and OpenTime <= {endTime} and SymbolId = {SymbolId} " +
                "group by OpenTime / 1000 / 60 / 60 / 24) days " +
                $"on c.OpenTime = days.CloseTime and SymbolId = {SymbolId} ", null, dr => //
            {
                result = (double)dr["MA19"];
            });

            return result;
        }


        // public Breakthrough() : base()
        public Breakthrough(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            // _maHoursInterval = Config.GetValue<int>("Strategies:MovingAverage:MaHourInterval");		
            _bullParams = new DealParams(
                AccountManager.Config.GetValue<double>("BullParams:DealProfit"),
                AccountManager.Config.GetValue<double>("BullParams:Threshold"),
                AccountManager.Config.GetValue<double>("BullParams:StopLoss"),
                AccountManager.Config.GetValue<double>("BullParams:ConfirmExtrPart")
            );

            _bearParams = new DealParams(
                AccountManager.Config.GetValue<double>("BearParams:DealProfit"),
                AccountManager.Config.GetValue<double>("BearParams:Threshold"),
                AccountManager.Config.GetValue<double>("BearParams:StopLoss"),
                AccountManager.Config.GetValue<double>("BearParams:ConfirmExtrPart")
            );

            _bearParamsValue = AccountManager.Config.GetValue<double>("BearParamsValue");
            _bullParamsValue = AccountManager.Config.GetValue<double>("BullParamsValue");

            _isBullParams = AccountManager.Config.GetValue<int>("IsBullParams") == 1;
            _isBearParams = AccountManager.Config.GetValue<int>("IsBearParams") == 1;

            _MA = GetMA(BnnUtils.GetUnixNow(), s_maInterval);
            BnnUtils.Log($"MA {s_maInterval}: {_MA}");
        }


        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            if (klines[^1].OpenTime % 60 == 0) _MA = GetMA(klines[^1].OpenTime, s_maInterval); // ) && (this is Breakthrough)

            var priceChange = (double)(CurrPrice > 0 ? (double)CurrPrice / _MA - 1 : 0);
            // var priceChange = CalcPriceChange(klines, klines.Count - 1, DefaultThresholdInterval);
            // var priceChange5 = 0; // CalcPriceChange(klines, klines.Count - 1, 5); // 3, 1? 16-08-24
            SetupParams(priceChange);

            // optimize productivity
            // if ((previousValue != -1) && (klines[klines.Count - 1 - _someValue].LowPrice >= _lastMin) && (klines[klines.Count - 1 - _someValue].HighPrice <= _lastMax)) return previousValue;

            var range = GetTradingRange(klines, klines.Count - 1 - 0, false); // previousValue == -1 --- true  --- _someValue

            var threshold = (range.Item2 - range.Item1) * _dealParams.PriceThreshold;
            var result = (decimal)(range.Item1 + threshold);

            _confirmExtrVal = (decimal)_dealParams.ConfirmExtrPart * result; //  extrs.Item1;

            if (AccountManager.OutLog && (result != previousValue))
            {
                var lastKlineDateTime = $"{BnnUtils.FormatUnixTime(klines[^1].OpenTime)}";
                BnnUtils.Log($"{lastKlineDateTime} min {range.Item1} max {range.Item2} ma{s_maInterval} {_MA:0.###} th {_dealParams.PriceThreshold} ( {((range.Item2 - range.Item1) * 100 / range.Item1):0.####}% ) => {result:0.##} - {_confirmExtrVal:0.##}");
            }
            return result;
        }

        protected override void SaveStatistics(List<BaseDealParams> tradeResults) // async Task<long>
        {
            var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, IsBull, IsBear, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var dp = (DealParams)tr;
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {dp.DealProfit}, {dp.PriceThreshold}, {dp.ConfirmExtrPart}, {dp.StopLossPerc}, {_isBullParams}, {_isBearParams}, {dp.TotalProfit}, 'b'), "; //  {k[5]}, {k[7]}, {k[8]}, {k[9]}),
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);
        }


        public override string GetName() { return "Breakthrough"; }

        protected override void SetupParams(double priceChange, double priceChangeMiddle = 0)
        {
            // bull
            if (_isBullParams && (priceChange > _bullParamsValue))  // 0.05 --- ) && (priceChangeMiddle >= 0) --- 0.1 --- _bullParamsValue
            {
                _dealParams.CopyFrom(_bullParams);
            }

            // bear
            else if (_isBearParams && (priceChange < _bearParamsValue)) // -0.03 --- -0.15 --- _bearParamsValue
            {
                _dealParams.CopyFrom(_bearParams);
            }

            // flat
            else
            {
                _dealParams.CopyFrom(InitialParams);
            }
        }


        /* public void FindBestBullValue()
        {
			double[] _bullValArr = [-0.03, -0.05, -0.11, -0.16, -0.19]; // , 7, 9 --- 1, 2, 3, 5, 9 --- 
			int[] _maIntervals = [3, 5, 8, 11, 15]; // 1, , 30, 300, 720

            Console.WriteLine($"BB {BnnUtils.FormatUnixTime(StartTime)} - {BnnUtils.FormatUnixTime(EndTime)}");
            _outLog = false;
            var deals = new List<DealParams>();
            var klines = LoadKlinesFromDB(StartTime - FindRangeKlinesCount() * 60L * 1000, EndTime);
            long counter = 0;
            long paramsSetCount = _maIntervals.Length * _bullValArr.Length;

            foreach (var bv in _bullValArr)
            {
                _bearParamsValue = bv;
                foreach (var mi in _maIntervals)
                {
                    MaInterval = mi;

                    var dp = new DealParams(InitialParams.DealProfit, InitialParams.PriceThreshold, InitialParams.StopLossPerc, InitialParams.ConfirmExtrPart);
                    dp.MaxDealInterval = MaInterval;
                    dp.ThresholdOffet = _bearParamsValue;
                    _dealParams = dp;
                    BackTest(klines);
                    _dealParams.CopyFrom(InitialParams);
                    deals.Add(dp);

                    counter++;
                    BnnUtils.ClearCurrentConsoleLine();
                    Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%"); // IsAdaptive --- OutLog
                }
            }

            List<DealParams> sortedParams = [.. deals.OrderByDescending(d => d.TotalProfit)];
            Console.WriteLine("\r\n");
            foreach (var p in sortedParams)
            {
                Console.WriteLine(p);
            };
        } */
    }
}