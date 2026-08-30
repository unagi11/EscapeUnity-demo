using System.Reflection;
using Escape.UI;
using NUnit.Framework;

namespace Escape.Tests.Editor
{
    public sealed class TimingCheckEvaluatorTests
    {
        [Test]
        public void SafeWindowIncludesItsBoundaries()
        {
            Assert.That(TimingCheckEvaluator.IsSuccess(0.4f, 0.4f, 0.6f), Is.True);
            Assert.That(TimingCheckEvaluator.IsSuccess(0.6f, 0.4f, 0.6f), Is.True);
        }

        [Test]
        public void OutsideSafeWindowFails()
        {
            Assert.That(TimingCheckEvaluator.IsSuccess(0.61f, 0.4f, 0.6f), Is.False);
        }

        [Test]
        public void RandomizedSafeWindowStartStaysWithinItsConfiguredRange()
        {
            MethodInfo getRandomizedStart = typeof(TimingCheckEvaluator).GetMethod(
                "GetRandomizedSafeWindowStart",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(getRandomizedStart, Is.Not.Null, "성공 범위를 무작위로 배치할 메서드가 필요합니다.");

            float start = (float)getRandomizedStart.Invoke(null, new object[] { 0.5f, 0.25f, 0.75f, 0.125f });
            float clampedStart = (float)getRandomizedStart.Invoke(null, new object[] { 1f, 0.8f, 0.95f, 0.3f });

            Assert.That(start, Is.EqualTo(0.5f));
            Assert.That(clampedStart, Is.EqualTo(0.7f));
        }
    }
}
