// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Bnncmd;
using DbSpace;
using Microsoft.Extensions.Configuration;

namespace Bnncmd.Strategy
{
    internal class TrParams : TrDealParams
    {
        public double DpCoeff { get; set; }
        public double DpOffset { get; set; }
        public double SlCoeff { get; set; }
        public double SlOffset { get; set; }
        public double LtCoeff { get; set; }
        public double LtOffset { get; set; }
        public double StCoeff { get; set; }
        public double StOffset { get; set; }


        public TrParams(double dpCoeff, double dpOffset, double slCoeff, double slOffset, double ltCoeff, double ltOffset, double stCoeff, double stOffset) : base(168, 1, 1, 0, 0)
        {
            DpCoeff = dpCoeff;
            DpOffset = dpOffset;
            SlCoeff = slCoeff;
            SlOffset = slOffset;
            LtCoeff = ltCoeff;
            LtOffset = ltOffset;
            StCoeff = stCoeff;
            StOffset = stOffset;

            MaHourInterval = 168;
            DiffInterval = 1;
            CheckRangeInterval = 1;

            DealProfit = 100;
            PriceThreshold = -100;
            StopLossPerc = 0.05; // 100;
            ConfirmExtrPart = 0.00001;
        }


        public override string ToString()
        {
            var results = $"{TotalProfit:0.##}\t\t{DealCount}\t{StopLossCount}\t\tta";
            var conditions = $"dc {DpCoeff}\tdo {DpOffset}\tlc {LtCoeff}\tlo {LtOffset}\tstc {StCoeff}\tsto {StOffset}\tslc {SlCoeff}\tslo {SlOffset}"; // $"dc {DpCoeff}\tdo {DpOffset}\tlc {LtCoeff}\tlo {LtOffset}\tso {StCoeff}\tso {StOffset}\tsc {SlCoeff}\tso {SlOffset}";
            return results + "\t" + conditions;
        }


        public override string GetParamsDesciption()
        {
            return $"dc {DpCoeff} do {DpOffset}   lc {LtCoeff} lo {LtOffset}   so {StCoeff} so {StOffset}   sc {SlCoeff} so {SlOffset}"; // $"mi {MaHourInterval}   di {DiffInterval}   cri {CheckRangeInterval}   sl -   dp -   lt -   st -\n\r";
            // return $"mi {MaHourInterval}   di {DiffInterval}   cri {CheckRangeInterval}   sl {StopLossPerc}   dp {DealProfit}   lt {LongThreshold}   st {ShortThreshold}\n\r";
        }
    }


    internal class MovingRange : MovingRangeBase
    {
        public MovingRange(string symbolName, AccountManager manager) : base(symbolName, manager)
        {
            var newDp = new TrParams(_dpCoeff, _dpOffset, _slCoeff, _slOffset, _ltCoeff, _ltOffset, _stCoeff, _stOffset);
            newDp.CopyFrom(_dealParams);
            _dealParams = newDp;
        }

        public override string GetName() { return "Moving Range"; }

        private double _dpCoeff = 0.0025;
        private double _dpOffset = 0.0055;
        private double _slCoeff = 0.015;
        private double _slOffset = 0.019;
        private double _ltCoeff = 1.33;
        private double _ltOffset = -1.0;

        // private double _slOffset = -0.39;
        // private double _ltOffset = 0.27;

        private double _stCoeff = 0.0245;
        private double _stOffset = 0.01;

        private readonly double _minProfit = 1.5 * (double)AccountManager.Fee;
        private readonly double _minSL = 0.005;

        private double GetTradeValue(List<Kline> klines, double previousValue = -1, bool getLong = true)
        {
            // if (CurrPrice == -1) CurrPrice = 
            _rangeKlines = klines;
            CalcTrendAngle(previousValue);

            var trendAngle = _trendAngle / 100;
            _dealParams.DealProfit = _dpOffset + _dpCoeff * trendAngle;
            if (_dealParams.DealProfit < _minProfit) _dealParams.DealProfit = _minProfit;

            _dealParams.StopLossPerc = _slOffset + _slCoeff * trendAngle;
            if (_dealParams.StopLossPerc < _minSL) _dealParams.StopLossPerc = _minSL;

            _longThreshold = _ltCoeff * trendAngle + _ltOffset;
            _shortThreshold = _stCoeff * trendAngle + _stOffset;

            var currentRange = GetRange(-1);
            double result;
            double thres;
            //if (getLong)
            //{
            result = currentRange.Item1;
            thres = _rangeWidth * _longThreshold; //_longThreshold;
            /*}
			else 
			{
				result = currentRange.Item2;
				thres = _rangeWidth * _shortThreshold;
			}*/

            // var outInfo = (klines[^1].OpenTime % ((15 - _detailing) * 1000 * 60) == 0) || (previousValue == -1);
            var priceOutThreshold = IsBacktest() ? 0.01 : 0.001;
            var outInfo = (Math.Abs(result + thres - previousValue) / previousValue > priceOutThreshold) || (previousValue == -1); // 0.0015

            _lastKlineInfo = $"{BnnUtils.FormatUnixTime(klines[^1].OpenTime)}";
            var maInfo = $"ta {_trendAngle:0.##} ma{_maHoursInterval} {_previousMaValue:0.###}->{_currHourMaValue:0.###}  lt {_longThreshold:0.###} st {_shortThreshold:0.###} dp {_dealParams.DealProfit * 100:0.##}%";
            _lastKlineInfo = $"{_lastKlineInfo} {SymbolName} {CurrPrice:0.###} {maInfo} min {currentRange.Item1:0.###} max {currentRange.Item2:0.###} tr {100 * _rangeWidth / result:0.####}% => {(result + thres):0.####}";
            if (AccountManager.OutLog && outInfo && !IsBacktest()) //  && (Math.Abs((result - CurrPrice) / result) > 0.05)
            {
                // var trends = (_dealParams as TrParams).TrendValues;
                // trends.Add(_trendAngle);
                BnnUtils.Log(_lastKlineInfo); // .## cp 
            }
            return result + thres;
        }


        protected override decimal GetLongValue(List<Kline> klines, decimal previousValue = -1)
        {
            return (decimal)GetTradeValue(klines, (double)previousValue);  //  * (1 + _rangeWidth * _longThreshold);
        }


        protected override void SaveStatistics(List<BaseDealParams> tradeResults)
        {
            var script = "INSERT INTO backtest (BeginDate, EndDate, SymbolId, Param1, Param2, Param3, Param4, Param5, Param6, IsBull, Balance, Strategy) VALUES ";
            foreach (var tr in tradeResults)
            {
                var rp = (TrParams)tr;
                // script += $"({StartTime}, {EndTime}, {SymbolId}, {rp.DpCoeff}, {rp.DpOffset}, {rp.LtCoeff}, {rp.LtOffset}, {rp.SlCoeff}, {rp.SlOffset}, 3, {rp.TotalProfit}, 'v'), ";
                script += $"({_manager.StartTime}, {_manager.EndTime}, {SymbolId}, {rp.DpCoeff}, {rp.DpOffset}, {rp.LtCoeff}, {rp.LtOffset}, {rp.StCoeff}, {rp.StOffset}, 4, {rp.TotalProfit}, 'v'), ";
            };
            script = string.Concat(script.AsSpan(0, script.Length - 2), ";");
            Console.WriteLine(script);
            DB.ExecQuery(_manager.DbConnection, script, null);
        }


        protected override List<BaseDealParams> CalculateParams(List<Kline> klines)
        {
            /* double[] dpOffsetArr = [0.0138];
            double[] dpCoeffArr = [0.00213];
			double[] ltOffsetArr = [-0.5];
            double[] ltCoeffArr = [1.919];*/

            /* double[] dpOffsetArr = [0.0050, 0.0055, 0.0060];
            double[] dpCoeffArr = [0.0020, 0,0025, 0.0030];
			double[] ltOffsetArr = [-0.85, -0.9, -0.95];
            double[] ltCoeffArr = [2.25, 2.3, 2.35]; */

            double[] dpOffsetArr = [0.0045, 0.0055, 0.0065];
            double[] dpCoeffArr = [0.0025];
            double[] ltOffsetArr = [-1];
            double[] ltCoeffArr = [1.33];

            double[] stOffsetArr = [-0.03, 0.0, -0.05]; // 0.01
            double[] stCoeffArr = [0.01, 0.0245, 0.035];	// 0.0245		
            // double[] slOffsetArr = [0.0331];
            // double[] slCoeffArr = [-0.01358];
            double[] slOffsetArr = [0.019];
            double[] slCoeffArr = [0.015];

            var deals = new List<BaseDealParams>();
            long paramsSetCount = dpOffsetArr.Length * dpCoeffArr.Length * ltOffsetArr.Length * ltCoeffArr.Length * stOffsetArr.Length * stCoeffArr.Length * slOffsetArr.Length * slCoeffArr.Length;
            long counter = 0;

            foreach (var dof in dpOffsetArr)
            {
                _dpOffset = dof;
                foreach (var dc in dpCoeffArr)
                {
                    _dpCoeff = dc;
                    foreach (var lo in ltOffsetArr)
                    {
                        _ltOffset = lo;
                        foreach (var lc in ltCoeffArr)
                        {
                            _ltCoeff = lc;
                            foreach (var sto in stOffsetArr)
                            {
                                _stOffset = sto;
                                foreach (var sc in stCoeffArr)
                                {
                                    _stCoeff = sc;
                                    foreach (var slo in slOffsetArr)
                                    {
                                        _slOffset = slo;
                                        foreach (var slc in slCoeffArr)
                                        {
                                            _slCoeff = slc;
                                            var dp = new TrParams(_dpCoeff, _dpOffset, _slCoeff, _slOffset, _ltCoeff, _ltOffset, _stCoeff, _stOffset);
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
                }
            }

            // Console.Write($"MovingRange.CaclulateParams - {deals.Count}");			
            return deals;
        }

    }
}
