#if UNITY_EDITOR || DEVELOPMENT_BUILD || ESCAPE_TESTFLIGHT
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Escape.QA
{
    // NUnit이 없는 플레이어 빌드에서도 QA DSL 검증 실패를 동일하게 중단시킨다.
    internal static class Assert
    {
        public static void That<T>(T actual, QaConstraint constraint, string message = null)
        {
            if (constraint == null || !constraint.Matches(actual))
            {
                throw new RuntimeQaAssertionException(
                    string.IsNullOrWhiteSpace(message)
                        ? $"QA 검증 실패: 실제값={Format(actual)}, 기대값={constraint?.Description ?? "constraint"}"
                        : message);
            }
        }

        public static void Fail(string message)
        {
            throw new RuntimeQaAssertionException(message);
        }

        private static string Format(object value)
        {
            return QaConstraint.IsNull(value) ? "null" : value.ToString();
        }
    }

    // 런타임 QA 실패를 실행기에서 구분해 표시하기 위한 예외다.
    public sealed class RuntimeQaAssertionException : Exception
    {
        public RuntimeQaAssertionException(string message) : base(message)
        {
        }
    }

    // QA 실행기에서 사용하는 최소 제약 조건 계약이다.
    internal abstract class QaConstraint
    {
        protected QaConstraint(string description)
        {
            Description = description;
        }

        public string Description { get; }
        public abstract bool Matches(object actual);

        internal static bool IsNull(object value)
        {
            return value == null || value is UnityEngine.Object unityObject && unityObject == null;
        }
    }

    // 단순 조건식을 함수로 감싸 NUnit 스타일 호출을 유지한다.
    internal sealed class PredicateQaConstraint : QaConstraint
    {
        private readonly Func<object, bool> predicate;

        public PredicateQaConstraint(string description, Func<object, bool> predicate) : base(description)
        {
            this.predicate = predicate;
        }

        public override bool Matches(object actual)
        {
            return predicate(actual);
        }
    }

    // 대소문자 무시 옵션을 지원하는 동등 비교다.
    internal sealed class EqualQaConstraint : QaConstraint
    {
        private readonly object expected;
        private bool ignoreCase;

        public EqualQaConstraint(object expected) : base($"equal to {expected}")
        {
            this.expected = expected;
        }

        public EqualQaConstraint IgnoreCase
        {
            get
            {
                ignoreCase = true;
                return this;
            }
        }

        public override bool Matches(object actual)
        {
            if (ignoreCase && actual is string actualText && expected is string expectedText)
            {
                return string.Equals(actualText, expectedText, StringComparison.OrdinalIgnoreCase);
            }

            return Equals(actual, expected);
        }
    }

    // Is.True, Is.EqualTo 등의 기존 QA 검증 문법을 플레이어에서 제공한다.
    internal static class Is
    {
        public static QaConstraint True { get; } = new PredicateQaConstraint("true", value => value is true);
        public static QaConstraint False { get; } = new PredicateQaConstraint("false", value => value is false);
        public static QaConstraint Null { get; } = new PredicateQaConstraint("null", QaConstraint.IsNull);
        public static QaNegatedConstraintFactory Not { get; } = new();

        public static EqualQaConstraint EqualTo(object expected)
        {
            return new EqualQaConstraint(expected);
        }

        public static QaConstraint GreaterThan(IComparable expected)
        {
            return new PredicateQaConstraint(
                $"> {expected}",
                actual => actual is IComparable comparable && comparable.CompareTo(expected) > 0);
        }

        public static QaConstraint GreaterThanOrEqualTo(IComparable expected)
        {
            return new PredicateQaConstraint(
                $">= {expected}",
                actual => actual is IComparable comparable && comparable.CompareTo(expected) >= 0);
        }

        public static QaConstraint InRange(IComparable minimum, IComparable maximum)
        {
            return new PredicateQaConstraint(
                $"between {minimum} and {maximum}",
                actual => actual is IComparable comparable &&
                          comparable.CompareTo(minimum) >= 0 &&
                          comparable.CompareTo(maximum) <= 0);
        }
    }

    // Is.Not.Null과 Is.Not.Empty를 제공한다.
    internal sealed class QaNegatedConstraintFactory
    {
        public QaConstraint Null { get; } = new PredicateQaConstraint("not null", value => !QaConstraint.IsNull(value));
        public QaConstraint Empty { get; } = new PredicateQaConstraint("not empty", value => !QaCollection.IsEmpty(value));
    }

    // Does.Contain과 Does.Not.Contain을 제공한다.
    internal static class Does
    {
        public static QaContainsConstraintFactory Not { get; } = new(true);

        public static QaConstraint Contain(object expected)
        {
            return new QaContainsConstraintFactory(false).Contain(expected);
        }
    }

    // 컬렉션 포함 여부 조건을 만든다.
    internal sealed class QaContainsConstraintFactory
    {
        private readonly bool negate;

        public QaContainsConstraintFactory(bool negate)
        {
            this.negate = negate;
        }

        public QaConstraint Contain(object expected)
        {
            return new PredicateQaConstraint(
                negate ? $"not contain {expected}" : $"contain {expected}",
                actual => QaCollection.Contains(actual, expected) != negate);
        }
    }

    // Has.Length.EqualTo와 Has.Count.EqualTo를 제공한다.
    internal static class Has
    {
        public static QaMetricConstraintFactory Length { get; } = new("length");
        public static QaMetricConstraintFactory Count { get; } = new("count");
    }

    // 컬렉션 길이 비교 조건을 만든다.
    internal sealed class QaMetricConstraintFactory
    {
        private readonly string metricName;

        public QaMetricConstraintFactory(string metricName)
        {
            this.metricName = metricName;
        }

        public QaConstraint EqualTo(int expected)
        {
            return new PredicateQaConstraint(
                $"{metricName} equal to {expected}",
                actual => QaCollection.GetCount(actual) == expected);
        }
    }

    // 문자열과 일반 컬렉션의 비어 있음, 포함, 개수를 공통 처리한다.
    internal static class QaCollection
    {
        public static bool IsEmpty(object value)
        {
            return GetCount(value) == 0;
        }

        public static bool Contains(object value, object expected)
        {
            if (value is string text && expected is string expectedText)
            {
                return text.Contains(expectedText, StringComparison.Ordinal);
            }

            if (value is not IEnumerable enumerable)
            {
                return false;
            }

            foreach (object item in enumerable)
            {
                if (Equals(item, expected))
                {
                    return true;
                }
            }

            return false;
        }

        public static int GetCount(object value)
        {
            if (value is string text)
            {
                return text.Length;
            }

            if (value is ICollection collection)
            {
                return collection.Count;
            }

            if (value is not IEnumerable enumerable)
            {
                return -1;
            }

            int count = 0;
            foreach (object _ in enumerable)
            {
                count++;
            }

            return count;
        }
    }
}
#endif

