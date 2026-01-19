// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using DbSpace;
using Microsoft.Extensions.Configuration;

namespace Bnncmd
{
    internal class MaDealParams : DealParams
    {
        public int MaHourInterval { get; set; }
        public int DiffInterval { get; set; }
        public double LongAngle { get; set; }
        public double ShortAngle { get; set; }

        public MaDealParams(int maHourInterval, int diffInterval, double longAngle, double shortAngle)
        {
            MaHourInterval = maHourInterval;
            DiffInterval = diffInterval;
            LongAngle = longAngle;
            ShortAngle = shortAngle;

            DealProfit = 100;
            PriceThreshold = -100;
            StopLossPerc = 100;
            ConfirmExtrPart = 0.00001;
        }

        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{MaxDealInterval}\t{DealCount}\t{StopLossCount}";
            var conditions = $"mi\t{MaHourInterval}\tdi\t{DiffInterval}\tla\t{LongAngle:0.###}\tsa\t{ShortAngle:0.###}";
            return results + "\t" + conditions;
        }

        public override string GetParamsDesciption()
        {
            return $"mi {MaHourInterval}   di {DiffInterval}   la {LongAngle:0.#####}   sa {ShortAngle}"; // \n\r
        }
    }


    internal class MovingAverage : TradeRangeBase
    {
        protected int _maHoursInterval = 72;
        protected int _maHourDiffInterval = 72;
        private double _longAngle; // = 1.55; // 0.5f;
        private double _shortAngle; // = -0.3;


        /*public MovingAverage() : base()
        {
            _maHoursInterval = AccountManager.Config.GetValue<int>("Strategies:MovingAverage:MaHourInterval");
            _maHourDiffInterval = AccountManager.Config.GetValue<int>("Strategies:MovingAverage:DiffInterval");
        }*/

        public MovingAverage(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            _maHoursInterval = AccountManager.Config.GetValue<int>("Strategies:MovingAverage:MaHourInterval");
            _maHourDiffInterval = AccountManager.Config.GetValue<int>("Strategies:MovingAverage:DiffInterval");

            _longAngle = AccountManager.Config.GetValue<double>("Strategies:MovingAverage:LongAngle");
            _shortAngle = AccountManager.Config.GetValue<double>("Strategies:MovingAverage:ShortAngle");

            var newDp = new MaDealParams(_maHoursInterval, _maHourDiffInterval, _longAngle, _shortAngle);
            newDp.CopyFrom(_dealParams);
            _dealParams = newDp;
        }

        protected double _previousMaValue = 0;
        protected double _lastHourMaValue = 0;
        protected double _currHourMaValue = 0;

        private double _lastOutAngleValue = 0;
        protected List<Kline>? _rangeKlines;
        protected double _trendAngle;


        public override string GetName() { return "Moving Average"; }


        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            _rangeKlines = klines;
            CalcTrendAngle((double)previousValue);

            if (_trendAngle > _longAngle)
            {
                if (AccountManager.OutLog) Console.WriteLine($"{BnnUtils.FormatUnixTime(_rangeKlines[^1].OpenTime)} {_trendAngle:0.####}%   MA{_maHoursInterval}-{_maHourDiffInterval}={_previousMaValue:0.####}"); //    MA{_maHoursInterval}-{0}={currentMaValue:0.####}
                return decimal.MaxValue;
            }
            else return 0;
        }


        protected override double GetShortValue(List<Kline> klines, double previousValue = -1)
        {
            _rangeKlines = klines;
            CalcTrendAngle(previousValue);

            if (_trendAngle <= _shortAngle) return 0;
            else return double.MaxValue;
        }


        protected override void SetupParams(double priceChange, double priceChangeMiddle = 0)
        {
            // do nothing
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, IsBull, IsBear, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var mp = (MaDealParams)tr;
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {mp.MaHourInterval}, {mp.DiffInterval}, {mp.LongAngle}, {mp.ShortAngle}, 0, 0, {mp.TotalProfit}, 'm'), "; //  {k[5]}, {k[7]}, {k[8]}, {k[9]}),
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            int[] maDaysIntervals = [24, 3 * 24, 7 * 24];
            int[] maHourDiffIntervals = [1, 1 * 24, 5 * 24];
            double[] longAngles = [0.0, 0.3, 1];
            double[] shortAngles = [-0.3, -0.1, 0.1];

            /* int[] maDaysIntervals = [3 * 24, 7 * 24]; 
			int[] maHourDiffIntervals = [1 * 24, 3 * 24];
			double[] longAngles = [0.0, 0.3, 1];
			double[] shortAngles = [-0.3]; */

            var deals = new List<BaseDealParams>();
            long paramsSetCount = maDaysIntervals.Length * maHourDiffIntervals.Length * longAngles.Length * shortAngles.Length;
            long counter = 0;
            foreach (var mi in maDaysIntervals)
            {
                _maHoursInterval = mi;
                foreach (var di in maHourDiffIntervals)
                {
                    _maHourDiffInterval = di;
                    foreach (var la in longAngles)
                    {
                        _longAngle = la;
                        foreach (var sa in shortAngles)
                        {
                            _shortAngle = sa;
                            var dp = new MaDealParams(mi, di, la, sa);
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
            return deals;
        }


        private double GetHourMA(int maInterval, int klineIndex)
        {
            if (_rangeKlines == null) throw new Exception("Klines are null");
            var lastHourTotalMins = _rangeKlines[klineIndex].OpenTime / 1000 / 60 / 60 * 60;
            var minsInLastHour = _rangeKlines[klineIndex].OpenTime / 1000 / 60 - lastHourTotalMins;
            var lastHourIndex = klineIndex - (int)minsInLastHour - 1;

            var sum = 0.0M;
            for (var i = 0; i < maInterval; i++)
            {
                sum += _rangeKlines[lastHourIndex].ClosePrice;
                lastHourIndex -= 60;
            }
            return (double)sum / maInterval;
        }


        protected double CalcTrendAngle(double previousValue = -1)
        {
            if (_rangeKlines == null) throw new Exception("Klines are null (CalcTrendAngle)");
            if ((previousValue == -1) || ((_rangeKlines[^1].OpenTime / 1000 / 60) % 60 < _checkDealPriceInterval)) // 
            {
                _lastHourMaValue = GetHourMA(_maHoursInterval - 1, _rangeKlines.Count - 1);
                _previousMaValue = GetHourMA(_maHoursInterval, _rangeKlines.Count - 1 - _maHourDiffInterval * 60);
                // Console.WriteLine($"new MA168 {_previousMaValue} => {_lastHourMaValue}");
            }

            _currHourMaValue = CurrPrice == -1 ? _lastHourMaValue : (_lastHourMaValue * (_maHoursInterval - 1) + (double)CurrPrice) / _maHoursInterval;
            _trendAngle = (_currHourMaValue - _previousMaValue) / _previousMaValue * 24 / _maHourDiffInterval * 100;

            if (AccountManager.OutLog && (Math.Abs((_trendAngle - _lastOutAngleValue) / _trendAngle) > 0.1)) // 0.3
            {
                _lastOutAngleValue = _trendAngle;
                var lastKlineDateTime = $"{BnnUtils.FormatUnixTime(_rangeKlines[^1].OpenTime)}";
            }

            return _trendAngle;
        }
    }
}