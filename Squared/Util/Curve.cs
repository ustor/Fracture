﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Linq.Expressions;

namespace Squared.Util {
    public interface ICurve<TValue> where TValue : struct {
        int Count { get; }
        float Start { get; }
        float End { get; }
        TValue GetValueAtPosition (float position);
        TValue this[float position] {
            get;
        }
        IEnumerable<CurvePoint<TValue>> Points {
            get;
        }
    }

    public struct CurvePoint<TValue> {
        public float Position;
        public TValue Value;

        public CurvePoint (float position, TValue value) {
            Position = position;
            Value = value;
        }
    }

    public abstract class CurveBase<TValue, TData> : IEnumerable<CurveBase<TValue, TData>.Point>, ICurve<TValue> 
        where TValue : struct
        where TData : struct 
    {
        public struct Window {
            public readonly CurveBase<TValue, TData> Curve;
            public readonly int FirstIndex, LastIndex;
            public readonly float Start, End;

            public TValue this[float position] {
                get {
                    if (position <= Start)
                        return Curve.GetValueAtIndex(FirstIndex);
                    else if (position >= End)
                        return Curve.GetValueAtIndex(LastIndex);
                    else
                        return Curve.GetValueAtPosition(position, FirstIndex, LastIndex);
                }
            }

            internal Window (CurveBase<TValue, TData> curve, int firstIndex, int lastIndex) {
                Curve = curve;
                FirstIndex = firstIndex;
                LastIndex = lastIndex;
                Start = Curve.GetPositionAtIndex(firstIndex);
                End = Curve.GetPositionAtIndex(lastIndex);
            }
        }

        public event EventHandler Changed;

        protected readonly List<Point> _Items = new List<Point>();
        protected Interpolator<TValue> _DefaultInterpolator;

        private PointPositionComparer _PositionComparer = new PointPositionComparer();

        public struct Point {
            public float Position;
            public TValue Value;
            public TData Data;
        }

        public class PointPositionComparer : IComparer<Point> {
            public int Compare (Point x, Point y) {
                return x.Position.CompareTo(y.Position);
            }
        }

        public float Start {
            get {
                return _Items[0].Position;
            }
        }

        public float End {
            get {
                return _Items[_Items.Count - 1].Position;
            }
        }

        public int Count {
            get {
                return _Items.Count;
            }
        }

        public int GetLowerIndexForPosition (float position) {
            return GetLowerIndexForPosition(position, 0, _Items.Count - 1);
        }

        protected int GetLowerIndexForPosition (float position, int firstIndex, int lastIndex) {
            int count = _Items.Count, max = count - 1;

            if (firstIndex < 0)
                firstIndex = 0;
            if (lastIndex >= count)
                lastIndex = max;

            if (firstIndex >= lastIndex)
                return firstIndex;

            int low = firstIndex;
            int high = lastIndex;
            int index;
            int nextIndex;

            if (count < 1)
                return firstIndex;

            if (_Items[lastIndex].Position < position)
                return lastIndex;
            else if (_Items[firstIndex].Position > position)
                return firstIndex;

            while (low <= high) {
                if (low == high)
                    return low;

                index = (low + high) / 2;
                nextIndex = (index >= max) ? max : index + 1;

                var indexItem = _Items[index];

                if (indexItem.Position < position) {
                    if (_Items[nextIndex].Position > position) {
                        return index;
                    } else {
                        low = index + 1;
                    }
                } else if (indexItem.Position == position) {
                    return index;
                } else {
                    high = index - 1;
                }
            }

            return count - 1;
        }
        
        public float GetPositionAtIndex (int index) {
            var max = _Items.Count - 1;
            index = (index < 0)
                ? 0
                : ((index > max)
                    ? max
                    : index);

            return _Items[index].Position;
        }

        public TData GetDataAtIndex (int index) {
            var max = _Items.Count - 1;
            index = (index < 0)
                ? 0
                : ((index > max)
                    ? max
                    : index);

            return _Items[index].Data;
        }

        public TValue GetValueAtIndex (int index) {
            var max = _Items.Count - 1;
            index = (index < 0)
                ? 0
                : ((index > max)
                    ? max
                    : index);

            return _Items[index].Value;
        }

        public TValue GetValueAtPosition (float position) {
            return GetValueAtPosition(position, 0, _Items.Count - 1);
        }

        protected abstract TValue GetValueAtPosition (float position, int firstIndex, int lastIndex);

        public void Clear () {
            _Items.Clear();

            OnChanged();
        }

        public void Clamp (float newStartPosition, float newEndPosition) {
            TValue newStartValue = GetValueAtPosition(newStartPosition);
            TValue newEndValue = GetValueAtPosition(newEndPosition);

            int i = 0;
            while (i < _Items.Count) {
                float position = _Items[i].Position;
                if ((position <= newStartPosition) || (position >= newEndPosition)) {
                    _Items.RemoveAt(i);
                } else {
                    i++;
                }
            }

            SetValueAtPositionInternal(newStartPosition, newStartValue, default(TData), false);
            SetValueAtPositionInternal(newEndPosition, newEndValue, default(TData), false);

            OnChanged();
        }

        public bool RemoveAtPosition (float position, float precision = 0.01f) {
            var index = GetLowerIndexForPosition(position);
            var item = _Items[index];
            if (Math.Abs(item.Position - position) > precision)
                return false;

            _Items.RemoveAt(index);

            if (_Items.Count == 0)
                _Items.Add(default(Point));

            OnChanged();
            return true;
        }

        protected void SetValueAtPositionInternal (float position, TValue value, TData data, bool dispatchEvent) {
            var oldIndex = GetLowerIndexForPosition(position);

            var newItem = new Point {
                Position = position,
                Value = value,
                Data = data
            };

            if ((oldIndex < _Items.Count) && (_Items[oldIndex].Position == position)) {
                _Items[oldIndex] = newItem;
            } else {
                _Items.Add(newItem);
                _Items.Sort(_PositionComparer);
            }

            if (dispatchEvent)
                OnChanged();
        }

        protected void OnChanged () {
            if (Changed != null)
                Changed(this, EventArgs.Empty);
        }

        public IEnumerator<Point> GetEnumerator () {
            return _Items.GetEnumerator();
        }

        public IEnumerable<CurvePoint<TValue>> Points {
            get {
                foreach (var item in _Items)
                    yield return new CurvePoint<TValue> {
                        Position = item.Position, 
                        Value = item.Value
                    };
            }
        }

        IEnumerator IEnumerable.GetEnumerator () {
            return _Items.GetEnumerator();
        }

        public TValue this[float position] {
            get {
                return GetValueAtPosition(position);
            }
        }

        public Window GetWindow (float lowPosition, float highPosition) {
            return GetWindow(
                GetLowerIndexForPosition(lowPosition),
                GetLowerIndexForPosition(highPosition) + 1
            );
        }

        public Window GetWindow (int firstIndex, int lastIndex) {
            return new Window(
                this,
                firstIndex,
                lastIndex
            );
        }
    }

    public class Curve<T> : CurveBase<T, Curve<T>.PointData>
        where T : struct {

        public struct PointData {
            public Interpolator<T> Interpolator; 
        }

        public Interpolator<T> DefaultInterpolator;

        protected InterpolatorSource<T> _InterpolatorSource;

        public Curve () {
            DefaultInterpolator = Interpolators<T>.Default;
            _InterpolatorSource = GetValueAtIndex;
        }

        public Curve (IEnumerable<CurvePoint<T>> points) 
            : this () {

            foreach (var kvp in points)
                Add(kvp.Position, kvp.Value);
        }

        public void SetValueAtPosition (float position, T value, Interpolator<T> interpolator = null) {
            SetValueAtPositionInternal(position, value, new PointData { Interpolator = interpolator }, true);
        }

        public void Add (float position, T value, Interpolator<T> interpolator = null) {
            SetValueAtPositionInternal(position, value, new PointData { Interpolator = interpolator }, true);
        }

        protected override T GetValueAtPosition (float position, int firstIndex, int lastIndex) {
            int index = GetLowerIndexForPosition(position, firstIndex, lastIndex);

            var lowerItem = _Items[index];
            var upperItem = _Items[(index == lastIndex) ? lastIndex : index + 1];

            var rangeSize = upperItem.Position - lowerItem.Position;
            if (rangeSize > 0) {
                float offset = (position - lowerItem.Position) / rangeSize;

                if (offset < 0.0f)
                    offset = 0.0f;
                else if (offset > 1.0f)
                    offset = 1.0f;

                var interpolator = _Items[index].Data.Interpolator ?? DefaultInterpolator;
                return interpolator(_InterpolatorSource, index, offset);
            } else {
                return lowerItem.Value;
            }
        }

        new public T this[float position] {
            get {
                return GetValueAtPosition(position);
            }
            set {
                SetValueAtPositionInternal(position, value, default(PointData), true);
            }
        }
    }

    public class HermiteSpline<T> : CurveBase<T, HermiteSpline<T>.PointData>
        where T : struct {

        public struct PointData {
            public T Velocity;
        }

        protected Interpolator<T> _Interpolator; 
        protected InterpolatorSource<T> _InterpolatorSource;

        protected static Arithmetic.BinaryOperatorMethod<T, T> _Sub;
        protected static Arithmetic.BinaryOperatorMethod<T, float> _Mul; 

        static HermiteSpline () {
            _Sub = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Subtract);
            _Mul = Arithmetic.GetOperator<T, float>(Arithmetic.Operators.Multiply);
        }

        public HermiteSpline () {
            _Interpolator = Interpolators<T>.Hermite;
            _InterpolatorSource = GetHermiteInputForIndex;
        }

        protected override T GetValueAtPosition (float position, int firstIndex, int lastIndex) {
            int index = GetLowerIndexForPosition(position, firstIndex, lastIndex);
            var lowerItem = _Items[index];
            var upperItem = _Items[Math.Min(index + 1, _Items.Count - 1)];

            if (lowerItem.Position < upperItem.Position) {
                float offset = (position - lowerItem.Position) / (upperItem.Position - lowerItem.Position);

                if (offset < 0.0f)
                    offset = 0.0f;
                else if (offset > 1.0f)
                    offset = 1.0f;

                return _Interpolator(_InterpolatorSource, (index * 2), offset);
            } else {
                return lowerItem.Value;
            }
        }

        private T GetHermiteInputForIndex (int index) {
            int quadIndex = index / 4, itemInQuad = index % 4;
            if (quadIndex < 0)
                quadIndex = 0;
            int aIndex = (quadIndex * 2), dIndex = aIndex + 1;

            switch (itemInQuad) {
                case 0: // A
                    return GetValueAtIndex(aIndex);
                case 1: // U
                    return GetDataAtIndex(aIndex).Velocity;
                case 2: // D
                    return GetValueAtIndex(dIndex);
                default:
                case 3: // V
                    return GetDataAtIndex(dIndex).Velocity;
            }
        }

        public void GetValuesAtIndex (int index, out float position, out T value, out T velocity) {
            position = GetPositionAtIndex(index);
            value = GetValueAtIndex(index);
            velocity = GetDataAtIndex(index).Velocity;
        }

        public bool GetValuesAtPosition (float position, out T value, out T velocity) {
            int index = GetLowerIndexForPosition(position);
            value = GetValueAtIndex(index);
            velocity = GetDataAtIndex(index).Velocity;

            return GetPositionAtIndex(index) == position;
        }

        public void SetValuesAtPosition (float position, T value, T velocity) {
            SetValueAtPositionInternal(position, value, new PointData { Velocity = velocity }, true);
        }

        public void Add (float position, T value, T velocity) {
            SetValueAtPositionInternal(position, value, new PointData { Velocity = velocity }, true);
        }

        public void ConvertToCardinal (float tension) {
            float tensionFactor = (1f / 2f) * (1f - tension);

            for (int start = 1, end = _Items.Count - 2, i = start; i <= end; i++) {
                var previous = _Items[i - 1];
                var pt = _Items[i];
                var next = _Items[i + 1];

                var tangent = _Sub(next.Value, previous.Value);
                pt.Data.Velocity = _Mul(tangent, tensionFactor);
                _Items[i] = pt;
            }
        }

        public static HermiteSpline<T> CatmullRom (IEnumerable<CurvePoint<T>> points) {
            return Cardinal(points, 0);
        }

        public static HermiteSpline<T> Cardinal (IEnumerable<CurvePoint<T>> points, float tension) {
            var result = new HermiteSpline<T>();

            foreach (var pt in points)
                result.Add(pt.Position, pt.Value, default(T));

            result.ConvertToCardinal(tension);

            return result;
        }
    }

    public static class CurveUtil {
        private static class Operators<T> {
            public static Arithmetic.BinaryOperatorMethod<T, T> Add, Sub;
            public static Arithmetic.BinaryOperatorMethod<T, float> Mul;

            static Operators () {
                Add = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Add);
                Sub = Arithmetic.GetOperator<T, T>(Arithmetic.Operators.Subtract);
                Mul = Arithmetic.GetOperator<T, float>(Arithmetic.Operators.Multiply);
            }
        }

        public static void CubicToHermite<T> (
            ref T a, ref T b,
            ref T c, ref T d,
            out T u, out T v
        ) {
            u = Operators<T>.Mul(Operators<T>.Sub(b, a), 3f);
            v = Operators<T>.Mul(Operators<T>.Sub(d, c), 3f);
        }

        public static void HermiteToCubic<T> (
            ref T a, ref T u,
            ref T d, ref T v,
            out T b, out T c
        ) {
            var multiplier = 1f / 3f;
            b = Operators<T>.Add(a, Operators<T>.Mul(u, multiplier));
            c = Operators<T>.Sub(d, Operators<T>.Mul(v, multiplier));
        }
    }

    public delegate float CurveSearchHeuristic<in T> (float position, T value) where T : struct;

    public static class CurveExtensions {
        public const int DefaultSearchSubdivision = 32;
        public const int DefaultMaxSearchRecursion = 7;
        public const float DefaultSearchEpsilon = float.Epsilon * 5;

        /// <summary>
        /// Searches the entire curve based on a heuristic.
        /// </summary>
        /// <param name="heuristic">A heuristic that measures how close a value is to the target value. This heuristic should return 0 if the target value is a match.</param>
        /// <returns>The position the search ended at if it was successful.</returns>
        public static float? Search<TValue> (
            this ICurve<TValue> curve, 
            CurveSearchHeuristic<TValue> heuristic
        )
            where TValue : struct
        {
            return Search(curve, heuristic, curve.Start, curve.End);
        }

        /// <summary>
        /// Searches a region of the curve based on a heuristic.
        /// </summary>
        /// <param name="heuristic">A heuristic that measures how close a value is to the target value. This heuristic should return 0 if the target value is a match.</param>
        /// <param name="low">The beginning of the search window.</param>
        /// <param name="high">The end of the search window.</param>
        /// <param name="subdivision">The number of sample points within the search window. A higher number of partitions will increase the likelihood that the best match will be found by the search, but increase the cost of the search.</param>
        /// <param name="maxRecursion">The maximum level of recursion for the search. High values for this argument will increase the precision of the search but also increase its cost.</param>
        /// <returns>The position the search ended at if it was successful.</returns>
        public static float? Search<TValue> (
            this ICurve<TValue> curve,
            CurveSearchHeuristic<TValue> heuristic, float low, float high,
            int? subdivision = null, int? maxRecursion = null,
            float? epsilon = null
        )
            where TValue : struct
        {
            float? result = null;
            float bestScore = float.MaxValue;

            SearchInternal(
                curve,
                heuristic,
                low, high,
                subdivision.GetValueOrDefault(DefaultSearchSubdivision),
                maxRecursion.GetValueOrDefault(DefaultMaxSearchRecursion),
                epsilon.GetValueOrDefault(DefaultSearchEpsilon),
                ref result, ref bestScore,
                0
            );

            return result;
        }

        private static void SearchInternal<TValue> (
            this ICurve<TValue> curve,
            CurveSearchHeuristic<TValue> heuristic,
            float low, float high,
            int subdivision, int maxRecursion,
            float epsilon,
            ref float? bestScoringPosition, ref float bestScore,
            int depth
        ) 
            where TValue : struct
        {
            if (subdivision < 2)
                subdivision = 2;

            if (high <= low)
                return;

            var actualSubdivision = subdivision;

            if (depth == 0) {
                actualSubdivision = Math.Min(1024, curve.Count * 2);
                actualSubdivision = Math.Max(actualSubdivision, subdivision);
            }

            bool improved = false;
            float partitionSize = (high - low) / actualSubdivision, partitionSizeHalf = partitionSize * 0.5f;
            if (Math.Abs(partitionSizeHalf) <= epsilon)
                return;

            for (int i = 0; i < actualSubdivision; i++) {
                var samplePosition = low + (partitionSize * i) + partitionSizeHalf;
                var score = heuristic(samplePosition, curve.GetValueAtPosition(samplePosition));

                if (score < bestScore) {
                    bestScore = score;
                    bestScoringPosition = samplePosition;
                    improved = true;
                }
            }

            if (depth >= maxRecursion)
                return;

            if (!improved)
                return;

            var newLow = Math.Max(low, bestScoringPosition.Value - partitionSizeHalf);
            var newHigh = Math.Min(high, bestScoringPosition.Value + partitionSizeHalf);

            SearchInternal(
                curve,
                heuristic,
                newLow, newHigh,
                subdivision, maxRecursion, epsilon,
                ref bestScoringPosition, ref bestScore,
                depth + 1
            );
        }

        /// <summary>
        /// Splits a hermite spline at a position. 
        /// Note that this may produce more than two output splines in order to eliminate discontinuities.
        /// </summary>
        /// <param name="splitPosition">The position at which to split the spline.</param>
        public static HermiteSpline<T>[] Split<T> (
            this HermiteSpline<T> spline,
            float splitPosition
        )
            where T : struct {
            var resultList = new List<HermiteSpline<T>>(4);

            spline.SplitInto(splitPosition, resultList);

            return resultList.ToArray();
        }

        /// <summary>
        /// Splits a hermite spline at a position. 
        /// Note that this may produce more than two output splines in order to eliminate discontinuities.
        /// </summary>
        /// <param name="splitPosition">The position at which to split the spline.</param>
        /// <param name="output">The list that will receive the new splines created by the split (up to 4).</param>
        public static void SplitInto<T> (
            this HermiteSpline<T> spline,
            float splitPosition,
            List<HermiteSpline<T>> output
        )
            where T : struct {

            if ((splitPosition <= spline.Start) || (splitPosition >= spline.End)) {
                output.Add(spline);
                return;
            }

            int count = spline.Count;
            int splitFirstPoint = spline.GetLowerIndexForPosition(splitPosition), splitSecondPoint = splitFirstPoint + 1;

            HermiteSpline<T> temp;

            if (splitFirstPoint > 0) {
                float position;
                T value, velocity;

                output.Add(temp = new HermiteSpline<T>());
                for (int i = 0, end = splitFirstPoint; i <= end; i++) {
                    spline.GetValuesAtIndex(i, out position, out value, out velocity);
                    temp.Add(position, value, velocity);
                }
            }

            float firstPosition = spline.GetPositionAtIndex(splitFirstPoint), secondPosition = spline.GetPositionAtIndex(splitSecondPoint);
            float splitLocalPosition = (splitPosition - firstPosition) / (secondPosition - firstPosition);

            T a = spline.GetValueAtIndex(splitFirstPoint), d = spline.GetValueAtIndex(splitSecondPoint);
            T u = spline.GetDataAtIndex(splitFirstPoint).Velocity, v = spline.GetDataAtIndex(splitSecondPoint).Velocity;
            T b, c;

            CurveUtil.HermiteToCubic(ref a, ref u, ref d, ref v, out b, out c);

            var ab = Arithmetic.Lerp(a, b, splitLocalPosition);
            var bc = Arithmetic.Lerp(b, c, splitLocalPosition);
            var cd = Arithmetic.Lerp(c, d, splitLocalPosition);

            var ab_bc = Arithmetic.Lerp(ab, bc, splitLocalPosition);
            var bc_cd = Arithmetic.Lerp(bc, cd, splitLocalPosition);

            var midpoint = Arithmetic.Lerp(ab_bc, bc_cd, splitLocalPosition);

            T newA, newB, newC, newD, newU, newV;

            newA = a;
            newB = ab;
            newC = ab_bc;
            newD = midpoint;

            CurveUtil.CubicToHermite(ref newA, ref newB, ref newC, ref newD, out newU, out newV);

            output.Add(temp = new HermiteSpline<T>());
            temp.Add(
                firstPosition, newA, newU
            );
            temp.Add(
                splitPosition, newD, newV
            );

            newA = midpoint;
            newB = bc_cd;
            newC = cd;
            newD = d;

            CurveUtil.CubicToHermite(ref newA, ref newB, ref newC, ref newD, out newU, out newV);

            output.Add(temp = new HermiteSpline<T>());
            temp.Add(
                splitPosition, newA, newU
            );
            temp.Add(
                secondPosition, newD, newV
            );

            if (splitSecondPoint < (count - 1)) {
                float position;
                T value, velocity;

                output.Add(temp = new HermiteSpline<T>());
                for (int i = splitSecondPoint, end = count - 1; i <= end; i++) {
                    spline.GetValuesAtIndex(i, out position, out value, out velocity);
                    temp.Add(position, value, velocity);
                }
            }
        }
    }
}
