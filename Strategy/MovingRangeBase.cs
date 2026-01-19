// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Bnncmd;
using DbSpace;
using Microsoft.Extensions.Configuration;

namespace Bnncmd.Strategy
{
    internal class TrDealParams : DealParams
    {
        public int CheckRangeInterval { get; set; }
        public int MaHourInterval { get; set; }
        public int DiffInterval { get; set; }
        public double LongThreshold { get; set; }
        public double ShortThreshold { get; set; }

        public List<double> TrendValues { get; set; } = new List<double>();

        public TrDealParams(int maHourInterval, int diffInterval, int checkRangeInterval, double longThreshold, double shortThreshold) // , int diffInterval, double longAngle, double shortAngle
        {
            MaHourInterval = maHourInterval;
            DiffInterval = diffInterval;
            CheckRangeInterval = checkRangeInterval;
            LongThreshold = longThreshold;
            ShortThreshold = shortThreshold;

            DealProfit = 100;
            PriceThreshold = -100;
            StopLossPerc = 0.05; // 100;
            ConfirmExtrPart = 0.00001;
        }

        public override string ToString()
        {
            // var results = $"{TotalProfit:0.##}\t\t{MaxDealInterval}\t{DealCount}\t{StopLossCount}";
            // var trendValuesCount = 
            var results = $"{TotalProfit:0.##}\t\t{DealCount}\t{StopLossCount}\t\tta {(TrendValues.Count == 0 ? 0 : TrendValues.Average()):0.###}";
            var conditions = $"mi {MaHourInterval}\tdi {DiffInterval}\tcri\t{CheckRangeInterval}\tsl\t{StopLossPerc}\tdp\t{DealProfit}\tlt\t{LongThreshold}\tst\t{ShortThreshold}"; // \tdi\t{DiffInterval}\tla\t{LongAngle:0.###}\tsa\t{ShortAngle:0.###}
            return results + "\t" + conditions;
        }

        public override string GetParamsDesciption()
        {
            return $"mi {MaHourInterval}   di {DiffInterval}   cri {CheckRangeInterval}   sl {StopLossPerc}   dp {DealProfit}   lt {LongThreshold}   st {ShortThreshold}\n\r";
            // return $"la {LongAngle:0.#####}   sa {ShortAngle}\n\r";
        }
    }

    internal class MovingRangeBase : MovingAverage
    {
        /*public MovingRangeBase() : base()
        {
            var newDp = new TrDealParams(_maHoursInterval, _maHourDiffInterval, _checkRangeInterval, _longThreshold, _shortThreshold);
            newDp.CopyFrom(_dealParams);
            _dealParams = newDp;
        }*/

        public MovingRangeBase(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            var newDp = new TrDealParams(_maHoursInterval, _maHourDiffInterval, _checkRangeInterval, _longThreshold, _shortThreshold);
            newDp.CopyFrom(_dealParams);
            _dealParams = newDp;
        }

        public override string GetName() { return "Moving Range Base"; }

        protected int _checkRangeInterval = 7 * 24 * 60;

        protected double _rangeWidth;

        protected double _longThreshold = -0.6; // 0.9;

        protected double _shortThreshold = -0.2;

        // protected double ShortPrice;

        protected (double, double) GetRange(int lastIndex = -1) // , bool checkCurrPrice = true
        {
            if (_rangeKlines == null) throw new Exception("Klines are null (GetRange)");
            double maxPrice = 0;
            double minPrice = int.MaxValue;
            if (lastIndex == -1) lastIndex = _rangeKlines.Count - 1;

            // var minPriceIndex = 0;
            var trendAngle = _trendAngle / 100 / 24 / 60;

            for (var i = lastIndex; i > lastIndex - _checkRangeInterval; i--)
            {
                var trendPrice = (double)_rangeKlines[i].HighPrice * (1 + (lastIndex - i + _checkDealPriceInterval) * trendAngle);
                if (trendPrice > maxPrice) maxPrice = trendPrice;

                trendPrice = (double)_rangeKlines[i].LowPrice * (1 + (lastIndex - i + _checkDealPriceInterval) * trendAngle);
                if (trendPrice < minPrice) minPrice = trendPrice;

                _rangeWidth = maxPrice - minPrice;
                // if ((i <= lastIndex - _checkRangeInterval) && (_rangeWidth / minPrice > 1.5 * Fee)) break;
                if (_rangeWidth / minPrice > _dealParams.DealProfit) break;
            }

            // var minPriceChange = _rangeKlines[minPriceIndex].LowPrice * (lastIndex - minPriceIndex + 1) * trendAngle;
            // _rangeWidth = (lastIndex - minPriceIndex + 1) * (maxPrice - minPrice) / Math.Sqrt(Math.Pow(lastIndex - minPriceIndex, 2) + Math.Pow(minPriceChange, 2));
            return (minPrice, maxPrice);
        }


        private double GetTradeValue(List<Kline> klines, double previousValue = -1, bool getLong = true)
        {
            _rangeKlines = klines;
            CalcTrendAngle(previousValue);
            var currentRange = GetRange(-1);
            double result;
            double thres;
            if (getLong)
            {
                result = currentRange.Item1;
                // _longThreshold = _trendAngle;
                // thres = _rangeWidth * _trendAngle * _longThreshold; //_longThreshold;
                thres = _rangeWidth * _longThreshold; //_longThreshold;

                // ShortPrice = currentRange.Item2 + _rangeWidth * _shortThreshold;
            }
            else
            {
                result = currentRange.Item2;
                thres = _rangeWidth * _shortThreshold;
            }

            var _detailing = 0;
            if (AccountManager.OutLog && (klines[^1].OpenTime % ((15 - _detailing) * 1000 * 60) == 0)) //  && (Math.Abs((result - CurrPrice) / result) > 0.05)
            {
                // var trends = (_dealParams as TrDealParams).TrendValues;
                // if ((trends.Count == 0)  || (trends[^1] != _lastHourMaValue)) 
                // trends.Add(_trendAngle);

                var lastKlineDateTime = $"{BnnUtils.FormatUnixTime(klines[^1].OpenTime)}";
                var maInfo = $"ta {_trendAngle:0.###} ma{_maHoursInterval} {_previousMaValue:0.###}->{_lastHourMaValue:0.###} ";
                BnnUtils.Log($"{lastKlineDateTime} {maInfo} min {currentRange.Item1:0.##} max {currentRange.Item2:0.##} cp {CurrPrice:0.##} tr {100 * _rangeWidth / result:0.####}% => {result:0.##} + {thres:0.##}"); // 
            }
            return result + thres;
        }


        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            return (decimal)GetTradeValue(klines, (double)previousValue, true);  //  * (1 + _rangeWidth * _longThreshold);
        }


        /* protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
			ShortPrice = LongPrice + _rangeWidth * (1 + _shortThreshold);
			if (AccountManager.OutLog && (previousValue != ShortPrice)) BnnUtils.Log($"short price {ShortPrice} (repeated - fix call from backtest)");
			return ShortPrice;
        }*/


        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            _shortPrice = (longPrice + (decimal)_rangeWidth * (1 + (decimal)_shortThreshold));
            // if (AccountManager.OutLog) BnnUtils.Log($"short price {ShortPrice}"); //  (initial)
            stopLossValue = longPrice * (1 - (decimal)_dealParams.StopLossPerc);
            return (double)_shortPrice;
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, Param5, IsBull, IsBear, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var rp = (TrDealParams)tr;
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {rp.LongThreshold}, {rp.ShortThreshold}, {rp.CheckRangeInterval}, {rp.StopLossPerc}, {rp.DealProfit}, {rp.MaHourInterval}, {rp.DiffInterval}, {rp.TotalProfit}, 'r'), ";
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            int[] maHourIntervals = [10, 24, 3 * 24, 7 * 24, 500];
            int[] maHourDiffIntervals = [1, 3, 1 * 24, 3 * 24, 5 * 24];
            // int[] checkRangeIntervals = [60, 3*60, 7 * 60, 12 * 60, 24 * 60]; // , 18 * 60, 48 * 60
            // int[] maHourIntervals = [7 * 24];
            // int[] maHourDiffIntervals = [1];
            // int[] checkRangeIntervals = [30, 1 * 60, 3 * 60]; // , 18 * 60, 48 * 60
            // double[] stopLossesArr = [0.015, 0.045, 0.07]; // 
            double[] stopLossesArr = [0.03]; // 
                                             // double[] stopLossesArr = [0.05];

            double[] profitsArr = [0.005]; //  ... , 0.04, 0.055, 0.07 5, 0.007, 0.01

            // double[] longThresholds = [0];
            // double[] shortThresholds = [0];
            // double[] longThresholds = [-1.5, -1.2, -1, -0.8, -0.6]; 	// bear
            double[] longThresholds = [-0.95];		// flat -0.85,  , -1.05
            // double[] longThresholds = [-0.3, 0, 0.5, 0.7, 0.9];		// bull
            // double[] longThresholds = [0.7, 1, 1.5, 1.9, 2.5];
            double[] shortThresholds = [0];
            // double[] shortThresholds = [-0.2, -0.1, 0, 0.1, 0.2];
            // double[] shortThresholds = [-0.3, 0, 0.3];

            var deals = new List<BaseDealParams>();
            long paramsSetCount = profitsArr.Length * maHourDiffIntervals.Length * maHourIntervals.Length * stopLossesArr.Length * longThresholds.Length * shortThresholds.Length;
            long counter = 0;
            foreach (var mi in maHourIntervals)
            {
                _maHoursInterval = mi;
                foreach (var di in maHourDiffIntervals)
                {
                    _maHourDiffInterval = di;
                    // foreach (var cri in checkRangeIntervals)
                    foreach (var pd in profitsArr)
                    {
                        // _checkRangeInterval = cri;
                        foreach (var lt in longThresholds)
                        {
                            _longThreshold = lt;
                            foreach (var st in shortThresholds)
                            {
                                _shortThreshold = st;
                                foreach (var sl in stopLossesArr)
                                {
                                    var dp = new TrDealParams(_maHoursInterval, _maHourDiffInterval, _checkRangeInterval, _longThreshold, _shortThreshold)
                                    {
                                        StopLossPerc = sl,
                                        DealProfit = pd
                                    };
                                    _dealParams = dp;
                                    // BackTest(klines);
                                    deals.Add(dp);

                                    counter++;
                                    BnnUtils.ClearCurrentConsoleLine();
                                    Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%");
                                }
                            }
                        }
                    }
                }
            }

            // Console.Write($"MovingRange.CaclulateParams - {deals.Count}");			
            return deals;
        }

    }
}
