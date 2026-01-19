// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Configuration;

namespace Bnncmd
{
    internal abstract class TradeRangeBase : BaseStrategy
    {
        protected abstract void SetupParams(double priceChange, double priceChangeMiddle = 0);

        protected bool _isAdaptive = false;


        public TradeRangeBase(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            _isAdaptive = AccountManager.Config.GetValue<int>("Adaptive") == 1;
        }

        /*private double CalcThreshold(List<Kline> klines)
        {
            var startPeriodClosePrice = klines[klines.Count - _thresholdInterval * AccountManager.OneDayKlines - 1].ClosePrice;
            var lastClosePrice = klines[klines.Count - 1].ClosePrice;
            var relativePriceGrowth = (lastClosePrice - startPeriodClosePrice) / startPeriodClosePrice;
            return Math.Round(relativePriceGrowth * _thresholdMultiplier) / 4 + _thresholdOffset;
        }*/


        //private readonly int _thresholdMultiplier = 35;
        protected const int DefaultThresholdInterval = 11;
        protected int _thresholdInterval = DefaultThresholdInterval;
        // private readonly double _thresholdOffset = 0; // -0.25;


        private static double CalcMovingAverage(List<Kline> klines, int index, int period = 7)
        {
            decimal sum = 0;
            for (var i = index - period; i < index; i++)
            {
                sum += klines[i].ClosePrice;
            }
            return (double)sum / period;
        }


        protected double CalcPriceChange(List<Kline> klines, int index, int depth = DefaultThresholdInterval)
        {
            var previousPeriodAvgPrice = CalcMovingAverage(klines, index - depth * AccountManager.OneDayKlines);
            var lastAvgPrice = CalcMovingAverage(klines, index);
            return (lastAvgPrice - previousPeriodAvgPrice) / previousPeriodAvgPrice;
        }


        /*public void CheckThreshold()
        {
            Console.WriteLine($"TH {BnnUtils.FormatUnixTime(StartTime)} - {BnnUtils.FormatUnixTime(EndTime)}\n\r");
            var klines = LoadKlinesFromDB(StartTime - FindRangeKlinesCount() * 60L * 1000, EndTime);
            var previousAvgPrice = 0.0;

            var bullStart = -1;
            var growthVal = 0.07;

            for (var i = FindRangeKlinesCount(); i < klines.Count; i++)
            {
                var previousPeriodAvgPrice = CalcMovingAverage(klines, i - _thresholdInterval * _oneDayKlines);
                var lastAvgPrice = CalcMovingAverage(klines, i);
                if (Math.Abs(lastAvgPrice - previousAvgPrice) / previousAvgPrice < 0.03) continue;  // to prevent rattle
                previousAvgPrice = lastAvgPrice;
                var relativePriceChange = (lastAvgPrice - previousPeriodAvgPrice) / previousPeriodAvgPrice;

                // Console.WriteLine($"{BnnUtils.FormatUnixTime(klines[i].OpenTime)}: {relativePriceChange} [{previousPeriodAvgPrice} - {lastAvgPrice}]");
                if ((Math.Abs(relativePriceChange) < growthVal) && (bullStart < 0)) bullStart = i; // bull - (relativePriceChange > growthVal) && (bullStart < 0)
                if ((bullStart > 0) && (Math.Abs(relativePriceChange) > growthVal)) // bull -  (relativePriceChange < growthVal)
                {
                    if (klines[i].OpenTime - klines[bullStart].OpenTime > 23 * _oneDayKlines * 60L * 1000) Console.WriteLine($"{BnnUtils.FormatUnixTime(klines[bullStart].OpenTime)} - {BnnUtils.FormatUnixTime(klines[i].OpenTime)}");
                    bullStart = -1;
                };
            }
        }*/


        /* public void FindBestThreshold()
        {
			int[] _evaluationIntervalArr = [3, 7, 11, 19, 29];
			int[] _multiplierArr = [1];
			double[] _thresholdOffsetArr = [-0.05, -0.07, -0.1, -0.15, -0.19]; // !!! - 0, 0.01, 
			
			// int[] _evaluationIntervalArr = [11];
			// int[] _multiplierArr = [9, 15, 19, 25, 30, 35, 40, 45];
			// int[] _evaluationIntervalArr = [7];
			// int[] _multiplierArr = [12];
			// double[] _thresholdOffsetArr = [0.05, 0.07, 0.1, 0.15, 0.19]; // !!! - 0, 0.01, 
			// double[] _thresholdOffsetArr = [-1, -0.75, -0.5, -0.25, 0, 0.25, 0.5, 0.75, 1]; // !!!
			// int[] _thresholdOffsetArr = [0]; // !!!

            _outLog = false;
            _isAdaptive = false;
            BackTest();
            Console.WriteLine($"\n\r{_dealParams.TotalProfit}\t{_dealParams.DealCount}\t{_dealParams.StopLossCount}\n\r");
            // return;

            _isAdaptive = true;
            Console.WriteLine($"TH {BnnUtils.FormatUnixTime(StartTime)} - {BnnUtils.FormatUnixTime(EndTime)}");
            _outLog = false;
            var deals = new List<DealParams>();
            long counter = 0;
            long paramsSetCount = _evaluationIntervalArr.Length * _multiplierArr.Length * _thresholdOffsetArr.Length;
            var klines = LoadKlinesFromDB(StartTime - FindRangeKlinesCount() * 60L * 1000, EndTime);

            foreach (var ei in _evaluationIntervalArr)
            {
                _thresholdInterval = ei;
                foreach (var m in _multiplierArr)
                {
                    _thresholdMultiplier = m;
                    foreach (var to in _thresholdOffsetArr)
                    {
                        _thresholdOffset = to;
                        var dp = new DealParams(_dealParams.DealProfit, 0, _dealParams.StopLossPerc, _dealParams.ConfirmExtrPart);
                        dp.ThresholdMultiplier = _thresholdMultiplier;
                        dp.MaxDealInterval = _thresholdInterval;
                        dp.ThresholdOffet = to;
                        _dealParams = dp;
                        BackTest(klines);
                        deals.Add(dp);

                        counter++;
                        BnnUtils.ClearCurrentConsoleLine();
                        Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%"); // IsAdaptive --- OutLog
                    }
                }
            }
            List<DealParams> sortedParams = [.. deals.OrderByDescending(d => d.TotalProfit)];

            Console.WriteLine("\r\n");
            foreach (var p in sortedParams)
            {
                Console.WriteLine($"interval: {p.MaxDealInterval}; mp: {p.ThresholdMultiplier:0.##}; off: {p.ThresholdOffet:0.##}: {p.TotalProfit}");
            };
        }*/


        protected (double, double) GetTradingRange(List<Kline> klines, int index = -1, bool checkCurrPrice = true)
        {
            double currMax = 0;
            double currMin = int.MaxValue;
            if (index == -1) index = klines.Count - 1;
            if (klines == null) return (currMax, currMin);

            for (var i = index; i > 0; i--)
            {
                if (klines[i].HighPrice > (decimal)currMax) currMax = (double)klines[i].HighPrice;
                if (klines[i].LowPrice < (decimal)currMin) currMin = (double)klines[i].LowPrice;
                if ((currMax - currMin) / currMin < (double)AccountManager.Fee + _dealParams.DealProfit + 2 * _dealParams.ConfirmExtrPart) continue; // 
                if (klines[i].LowPrice - (decimal)currMin < AccountManager.Fee / 5) continue;
                if ((decimal)currMax - klines[i].HighPrice < AccountManager.Fee / 5) continue;
                break;
            }

            if (checkCurrPrice)
            {
                if ((double)CurrPrice > currMax) currMax = (double)CurrPrice;
                if ((CurrPrice > -1) && ((double)CurrPrice < currMin)) currMin = (double)CurrPrice;
            }
            return (currMin, currMax);
        }


        protected override double GetShortValue(decimal longPrice, out decimal stopLossValue)
        {
            var shortPrice = longPrice * (1 + (decimal)AccountManager.Fee + (decimal)_dealParams.DealProfit) + _confirmExtrVal;
            stopLossValue = longPrice * (1 - (decimal)_dealParams.StopLossPerc);
            return (double)shortPrice;
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            int[] _someValueArr = [0]; // , 7, 9 --- 1, 2, 3, 5, 9 --- , 1, 2, 3, 5
            int[] _checkRangeIntervalArr = [1]; // 1, , 30, 300, 720

            double[] _profitsArr = [0.001, 0.003, 0.005, 0.009]; //  ... , 0.04, 0.055, 0.07
            double[] _stopLossesArr = [0.035, 0.045, 0.055];
            double[] _thresholdsArr = [-0.95, -0.75, -0.55]; // ...  , 0.15 ... 0.21, 0.45, 0.75, 1.0

            double[] _confirmExtrParts = [0.00001];

            /* double[] _profitsArr = [0.003, 0.007, 0.011, 0.015, 0.021, 0.029, 0.04, 0.055, 0.07, 0.11];
			double[] _stopLossesArr = [0.001, 0.01, 0.019, 0.045, 0.07];
			double[] _thresholdsArr = [-1.0, -0.75, -0.45, -0.21, -0.15, -0.1, -0.05, -0.03, 0, 0.03, 0.05, 0.1, 0.15, 0.21, 0.45, 0.75, 1.0];
			double[] _confirmExtrParts = [0, 0.00001, 0.0001, 0.0003, 0.0009, 0.0015, 0.003]; 
			double[] _confirmExtrParts = [0.00001];

			double[] _profitsArr = [0.007, 0.011, 0.015, 0.021, 0.029]; //  ... , 0.04, 0.055, 0.07
			double[] _stopLossesArr = [0.019, 0.045, 0.07];
			double[] _thresholdsArr = [-1.5, -1.0, -0.75, -0.45, -0.21, 0, 0.21]; // ...  , 0.15 ... 0.21, 0.45, 0.75, 1.0 

			double[] _profitsArr = [0.028, 0.029]; // 0.029
			double[] _stopLossesArr = [0.041, 0.045]; // 0.045
			double[] _thresholdsArr = [-0.22, -0.21]; // -0.21
			double[] _confirmExtrParts = [0.00001];

			double[] _profitsArr = [0.028]; // , 0.04, 0.055, 0.07
			double[] _stopLossesArr = [0.040, 0.041, 0.042, 0.043, 0.045];
			double[] _stopLossesArr = [0.041];
			double[] _thresholdsArr = [-0.24, -0.23, -0.22, -0.21, -0.20]; // 
			double[] _thresholdsArr = [-0.05, 0, 0.05]; // 
			double[] _confirmExtrParts = [0.00001];*/

            var deals = new List<BaseDealParams>();
            long paramsSetCount = _profitsArr.Length * _stopLossesArr.Length * _thresholdsArr.Length * _confirmExtrParts.Length * _checkRangeIntervalArr.Length * _someValueArr.Length;
            long counter = 0;
            foreach (var sv in _someValueArr)
            {
                _someValue = sv;
                foreach (var ri in _checkRangeIntervalArr)
                {
                    _checkDealPriceInterval = ri;
                    foreach (var pr in _profitsArr)
                    {
                        foreach (var sl in _stopLossesArr)
                        {
                            foreach (var th in _thresholdsArr)
                            {
                                foreach (var ce in _confirmExtrParts)
                                {
                                    var dp = new DealParams(pr, th, sl, ce);
                                    dp.MaxDealInterval = (long)(_someValue); // Math.Truncate
                                    _dealParams = dp;
                                    // BackTest(klines);
                                    _dealParams.CopyFrom(InitialParams);
                                    deals.Add(dp);

                                    counter++;
                                    BnnUtils.ClearCurrentConsoleLine();
                                    Console.Write($"{counter * 100.0 / paramsSetCount:0.##}%"); // IsAdaptive --- OutLog
                                }
                            }
                        }
                    }
                }
            }
            return deals;
        }
    }
}